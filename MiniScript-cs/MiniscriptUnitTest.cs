/*	MiniscriptUnitTest.cs

This file contains a number of unit tests for various parts of the MiniScript
architecture.  It's used by the MiniScript developers to ensure we don't
break something when we make changes.

You can safely ignore this, but if you really want to run the tests yourself,
just call Miniscript.UnitTest.Run().

*/
using System;
using System.Collections.Generic;

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

		static Interpreter RunScript(string source, bool legacyNumericBooleans, out List<string> output) {
			var miniscript = new Interpreter(source);
			miniscript.legacyNumericBooleans = legacyNumericBooleans;
			var capturedOutput = new List<string>();
			output = capturedOutput;
			miniscript.standardOutput = (s, eol) => capturedOutput.Add(s);
			miniscript.implicitOutput = miniscript.standardOutput;
			miniscript.errorOutput = (s, eol) => ReportError("Runtime error: " + s);
			miniscript.RunUntilDone(10, false);
			ErrorIf(!miniscript.done, "Script did not finish");
			return miniscript;
		}

		static void CheckOutput(string source, bool legacyNumericBooleans, params string[] expected) {
			List<string> output;
			RunScript(source, legacyNumericBooleans, out output);
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

		static void RunRuntimeUnitTests() {
			// Legacy mode preserves numeric-style output for booleans.
			CheckOutput("print true\nprint false\nprint true == false", true, "1", "0", "0");
			CheckOutput("print true\nprint false\nprint true == false", false, "true", "false", "false");

			// Verify logical behavior in legacy (fuzzy) vs strict mode.
			CheckOutput("print 0.5 and 0.5\nprint 0.5 or 0.5\nprint not 0.5", true, "0.25", "0.75", "0.5");
			CheckOutput("print 0.5 and 0.5\nprint 0.5 or 0.5\nprint not 0.5", false, "true", "true", "false");

			// Bool type map is available and works with isa.
			CheckOutput("print true isa bool\nprint false isa bool\nprint 1 isa bool", true, "1", "1", "0");
			CheckOutput("print true isa bool\nprint false isa bool\nprint 1 isa bool", false, "true", "true", "false");

			// Strict mode should produce bool-typed results for logical/comparison operators.
			List<string> strictOutput;
			var strict = RunScript("a = true and false\nb = 2 > 1\nc = not 0\nd = true or false\ne = true == true", false, out strictOutput);
			CheckGlobalBool(strict.GetGlobalValue("a"), false, "a");
			CheckGlobalBool(strict.GetGlobalValue("b"), true, "b");
			CheckGlobalBool(strict.GetGlobalValue("c"), true, "c");
			CheckGlobalBool(strict.GetGlobalValue("d"), true, "d");
			CheckGlobalBool(strict.GetGlobalValue("e"), true, "e");

			// Legacy mode still prints map.push values as numeric-style truth.
			CheckOutput("m = {}\nm.push 42\nprint m[42]", true, "1");
			CheckOutput("m = {}\nm.push 42\nprint m[42]", false, "true");
		}

		public static void Run() {
			Reset();
			Lexer.RunUnitTests();
			Parser.RunUnitTests();
			RunRuntimeUnitTests();
		}
	}
}

