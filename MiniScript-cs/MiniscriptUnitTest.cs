/*	MiniscriptUnitTest.cs

This file contains a number of unit tests for various parts of the MiniScript
architecture.  It's used by the MiniScript developers to ensure we don't
break something when we make changes.

You can safely ignore this, but if you really want to run the tests yourself,
just call Miniscript.UnitTest.Run().

*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Miniscript {
	public static class UnitTest {
		static int errorCount;

		public static bool HasFailures {
			get { return errorCount > 0; }
		}

		public static void Reset() {
			errorCount = 0;
		}

		public static void ReportError(string err) {
			// Set a breakpoint here if you want to drop into the debugger
			// on any unit test failure.
			errorCount++;
			Console.WriteLine(err);
			#if UNITY_EDITOR
			UnityEngine.Debug.LogError("Miniscript unit test failed: " + err);
			#endif
		}

		public static void ErrorIf(bool condition, string err) {
			if (condition) ReportError(err);
		}

		public static void ErrorIfNull(object obj) {
			if (obj == null) ReportError("Unexpected null");
		}

		public static void ErrorIfNotNull(object obj) { 
			if (obj != null) ReportError("Expected null, but got non-null");
		}

		public static void ErrorIfNotEqual(string actual, string expected,
			string desc="Expected {1}, got {0}") {
			if (actual == expected) return;
			ReportError(string.Format(desc, actual, expected));
		}

		public static void ErrorIfNotEqual(float actual, float expected,
			string desc="Expected {1}, got {0}") {
			if (actual == expected) return;
			ReportError(string.Format(desc, actual, expected));
		}

		static async Task<(Interpreter miniscript, List<string> output)> RunScript(string source, bool legacyNumericBooleans) {
			var miniscript = new Interpreter(source);
			miniscript.legacyNumericBooleans = legacyNumericBooleans;
			var capturedOutput = new List<string>();
			miniscript.standardOutput = (s, eol) => capturedOutput.Add(s);
			miniscript.implicitOutput = miniscript.standardOutput;
			miniscript.errorOutput = (s, eol) => ReportError("Runtime error: " + s);
			await miniscript.RunUntilDone().ConfigureAwait(false);
			ErrorIf(!miniscript.done, "Script did not finish");
			return (miniscript, capturedOutput);
		}

		static async Task CheckOutput(string source, bool legacyNumericBooleans, params string[] expected) {
			var run = await RunScript(source, legacyNumericBooleans).ConfigureAwait(false);
			List<string> output = run.output;
			ErrorIf(output.Count != expected.Length,
				string.Format("Expected {0} output lines, got {1}", expected.Length, output.Count));
			int max = Math.Min(output.Count, expected.Length);
			for (int i = 0; i < max; i++) {
				ErrorIfNotEqual(output[i], expected[i],
					string.Format("Output line {0}: expected {{1}}, got {{0}}", i + 1));
			}
		}

		static void CheckGlobalBool(Value value, bool expected, string name) {
			ErrorIf(!(value is ValBool), string.Format("Expected {0} to be ValBool, but got {1}",
				name, value == null ? "null" : value.GetType().Name));
			if (value is ValBool) {
				ErrorIf(((ValBool)value).value != expected,
					string.Format("Expected {0} to be {1}", name, expected));
			}
		}

		static async Task RunRuntimeUnitTests() {
			// Legacy mode preserves numeric-style output for booleans.
			await CheckOutput("print true\nprint false\nprint true == false", true, "1", "0", "0").ConfigureAwait(false);
			await CheckOutput("print true\nprint false\nprint true == false", false, "true", "false", "false").ConfigureAwait(false);

			// Verify logical behavior in legacy (fuzzy) vs strict mode.
			await CheckOutput("print 0.5 and 0.5\nprint 0.5 or 0.5\nprint not 0.5", true, "0.25", "0.75", "0.5").ConfigureAwait(false);
			await CheckOutput("print 0.5 and 0.5\nprint 0.5 or 0.5\nprint not 0.5", false, "true", "true", "false").ConfigureAwait(false);

			// Bool type map is available and works with isa.
			await CheckOutput("print true isa bool\nprint false isa bool\nprint 1 isa bool", true, "1", "1", "0").ConfigureAwait(false);
			await CheckOutput("print true isa bool\nprint false isa bool\nprint 1 isa bool", false, "true", "true", "false").ConfigureAwait(false);

			// Strict mode should produce bool-typed results for logical/comparison operators.
			var strictRun = await RunScript("a = true and false\nb = 2 > 1\nc = not 0\nd = true or false\ne = true == true", false).ConfigureAwait(false);
			var strict = strictRun.miniscript;
			CheckGlobalBool(strict.GetGlobalValue("a"), false, "a");
			CheckGlobalBool(strict.GetGlobalValue("b"), true, "b");
			CheckGlobalBool(strict.GetGlobalValue("c"), true, "c");
			CheckGlobalBool(strict.GetGlobalValue("d"), true, "d");
			CheckGlobalBool(strict.GetGlobalValue("e"), true, "e");

			// Legacy mode still prints map.push values as numeric-style truth.
			await CheckOutput("m = {}\nm.push 42\nprint m[42]", true, "1").ConfigureAwait(false);
			await CheckOutput("m = {}\nm.push 42\nprint m[42]", false, "true").ConfigureAwait(false);

			// Lambda factory should map lambda parameters to intrinsic args by name.
			const string lambdaAddName = "__unit_lambda_add";
			if (Intrinsic.GetByName(lambdaAddName) == null) {
				Intrinsic.CreateFromLambda(lambdaAddName, (double x, double y) => x + y);
			}
			await CheckOutput("print " + lambdaAddName + "(2,3)", false, "5").ConfigureAwait(false);

			// object-typed lambda parameters should still receive unwrapped CLR primitives.
			const string lambdaObjKindName = "__unit_lambda_obj_kind";
			if (Intrinsic.GetByName(lambdaObjKindName) == null) {
				Intrinsic.CreateFromLambda(lambdaObjKindName, (object x) => {
					if (x == null) return "null";
					if (x is double) return "double";
					if (x is bool) return "bool";
					if (x is string) return "string";
					return "value";
				});
			}
			await CheckOutput(
				"print " + lambdaObjKindName + "(3)\n" +
				"print " + lambdaObjKindName + "(true)\n" +
				"print " + lambdaObjKindName + "(\"foo\")\n" +
				"print " + lambdaObjKindName + "([])",
				false,
				"double",
				"bool",
				"string",
				"value"
			).ConfigureAwait(false);

			// Lambda factory should convert ValList to typed List<T> parameters.
			const string lambdaListSumName = "__unit_lambda_list_sum";
			if (Intrinsic.GetByName(lambdaListSumName) == null) {
				Intrinsic.CreateFromLambda(lambdaListSumName, (List<double> xs) => xs.Sum());
			}
			await CheckOutput("print " + lambdaListSumName + "([1,2,3])", false, "6").ConfigureAwait(false);

			// Sync-only run should throw if an intrinsic returns an incomplete async task.
			const string asyncNeverName = "__unit_async_never";
			if (Intrinsic.GetByName(asyncNeverName) == null) {
				Intrinsic.Create(asyncNeverName, (context) => {
					var tcs = new TaskCompletionSource<Value>();
					return tcs.Task;
				});
			}
			var syncOnly = new Interpreter("x = " + asyncNeverName);
			bool syncOnlyThrew = false;
			try {
				syncOnly.RunUntilDoneSyncOnly();
			} catch (RuntimeException re) {
				syncOnlyThrew = re.Message.Contains("sync-only mode");
			}
			ErrorIf(!syncOnlyThrew, "RunUntilDoneSyncOnly did not throw on incomplete async intrinsic");
		}

		public static async Task Run() {
			Reset();
			Lexer.RunUnitTests();
			Parser.RunUnitTests();
			await RunRuntimeUnitTests().ConfigureAwait(false);
		}
	}
}
