/*	MiniscriptInterpreter.cs

The only class in this file is Interpreter, which is your main interface 
to the MiniScript system.  You give Interpreter some MiniScript source 
code, and tell it where to send its output (via delegate functions called
TextOutputMethod).  Then you typically call RunUntilDone, which returns 
when either the script has stopped or cancellation is requested.  

For details, see Chapters 1-3 of the MiniScript Integration Guide.
*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Miniscript {

	/// <summary>
	/// TextOutputMethod: a delegate used to return text from the script
	/// (e.g. normal output, errors, etc.) to your C# code.
	/// </summary>
	/// <param name="output"></param>
	public delegate void TextOutputMethod(string output, bool addLineBreak);

	/// <summary>
	/// Interpreter: an object that contains and runs one MiniScript script.
	/// </summary>
	public class Interpreter {
		
		/// <summary>
		/// standardOutput: receives the output of the "print" intrinsic.
		/// </summary>
		public TextOutputMethod standardOutput {
			get {
				return _standardOutput;
			}
			set {
				_standardOutput = value;
				if (vm != null) vm.standardOutput = value;
			}
		}
		
		/// <summary>
		/// implicitOutput: receives the value of expressions entered when
		/// in REPL mode.  If you're not using the REPL() method, you can
		/// safely ignore this.
		/// </summary>
		public TextOutputMethod implicitOutput;
		
		/// <summary>
		/// errorOutput: receives error messages from the runtime.  (This happens
		/// via the ReportError method, which is virtual; so if you want to catch
		/// the actual exceptions rather than get the error messages as strings,
		/// you can subclass Interpreter and override that method.)
		/// </summary>
		public TextOutputMethod errorOutput;
		
			/// <summary>
			/// hostData is just a convenient place for you to attach some arbitrary
			/// data to the interpreter.  It gets passed through to the context object,
			/// so you can access it inside your custom intrinsic functions.  Use it
			/// for whatever you like (or don't, if you don't feel the need).
			/// </summary>
			public object hostData;

		/// <summary>
		/// done: returns true when we don't have a virtual machine, or we do have
		/// one and it is done (has reached the end of its code).
		/// </summary>
		public bool done {
			get { return vm == null || vm.done; }	
		}
		
		/// <summary>
		/// vm: the virtual machine this interpreter is running.  Most applications will
		/// not need to use this, but it's provided for advanced users.
		/// </summary>
		public TAC.Machine vm;
		
		TextOutputMethod _standardOutput;
		string source;
		Parser parser;
		
		/// <summary>
		/// Constructor taking some MiniScript source code, and the output delegates.
		/// </summary>
		public Interpreter(string source=null, TextOutputMethod standardOutput=null, TextOutputMethod errorOutput=null) {
			this.source = source;
			if (standardOutput == null) standardOutput = (s,eol) => Console.WriteLine(s);
			if (errorOutput == null) errorOutput = (s,eol) => Console.WriteLine(s);
			this.standardOutput = standardOutput;
			this.errorOutput = errorOutput;
		}
		
		/// <summary>
		/// Constructor taking source code in the form of a list of strings.
		/// </summary>
		public Interpreter(List<string> source) : this(string.Join("\n", source.ToArray())) {
		}
		
		/// <summary>
		/// Constructor taking source code in the form of a string array.
		/// </summary>
		public Interpreter(string[] source) : this(string.Join("\n", source)) {
		}
		
		/// <summary>
		/// Stop the virtual machine, and jump to the end of the program code.
		/// Also reset the parser, in case it's stuck waiting for a block ender.
		/// </summary>
		public void Stop() {
			if (vm != null) vm.Stop();
			if (parser != null) parser.PartialReset();
		}
		
		/// <summary>
		/// Reset the interpreter with the given source code.
		/// </summary>
		/// <param name="source"></param>
		public void Reset(string source="") {
			this.source = source;
			parser = null;
			vm = null;
		}
		
		/// <summary>
		/// Compile our source code, if we haven't already done so, so that we are
		/// either ready to run, or generate compiler errors (reported via errorOutput).
		/// </summary>
		public void Compile() {
			if (vm != null) return;	// already compiled

			if (parser == null) parser = new Parser();
				try {
					parser.Parse(source);
					vm = parser.CreateVM(standardOutput);
					vm.interpreter = new WeakReference(this);
				} catch (MiniscriptException mse) {
				ReportError(mse);
				if (vm == null) parser = null;
			}
		}
		
		/// <summary>
		/// Reset the virtual machine to the beginning of the code.  Note that this
		/// does *not* reset global variables; it simply clears the stack and jumps
		/// to the beginning.  Useful in cases where you have a short script you
		/// want to run over and over, without recompiling every time.
		/// </summary>
		public void Restart() {
			if (vm != null) vm.Reset();			
		}
		
		/// <summary>
		/// Run the compiled code until we either reach the end, or cancellation is requested.
		/// 
		/// Note that this method first compiles the source code if it wasn't compiled
		/// already, and in that case, may generate compiler errors.  And of course
		/// it may generate runtime errors while running.  In either case, these are
		/// reported via errorOutput.
		/// </summary>
		/// <param name="cancellationToken">cancellation token for stopping execution</param>
		public async Task RunUntilDone(CancellationToken cancellationToken=default) {
			int startImpResultCount = 0;
			try {
				if (vm == null) {
					Compile();
					if (vm == null) return;	// (must have been some error)
				}
				startImpResultCount = vm.globalContext.implicitResultCounter;
				while (!vm.done) {
					cancellationToken.ThrowIfCancellationRequested();
					ValueTask step = vm.Step(cancellationToken);		// update the machine
					if (!step.IsCompletedSuccessfully) {
						await step.ConfigureAwait(false);
					}
				}
			} catch (MiniscriptException mse) {
				ReportError(mse);
				Stop(); // was: vm.GetTopContext().JumpToEnd();
			}
			CheckImplicitResult(startImpResultCount);
		}
		
		/// <summary>
		/// Run one step of the virtual machine.  This method is not very useful
		/// except in special cases; usually you will use RunUntilDone (above) instead.
		/// </summary>
		public async Task Step(CancellationToken cancellationToken=default) {
			try {
				Compile();
				cancellationToken.ThrowIfCancellationRequested();
				ValueTask step = vm.Step(cancellationToken);
				if (!step.IsCompletedSuccessfully) {
					await step.ConfigureAwait(false);
				}
			} catch (MiniscriptException mse) {
				ReportError(mse);
				Stop(); // was: vm.GetTopContext().JumpToEnd();
			}
		}

		/// <summary>
		/// Run the compiled code without allowing asynchronous waits.  If any intrinsic
		/// returns an incomplete Task/ValueTask, this method throws.
		/// </summary>
		public void RunUntilDoneSyncOnly(CancellationToken cancellationToken=default) {
			int startImpResultCount = 0;
			if (vm == null) {
				Compile();
				if (vm == null) return;	// (must have been some error)
			}
			startImpResultCount = vm.globalContext.implicitResultCounter;
			while (!vm.done) {
				cancellationToken.ThrowIfCancellationRequested();
				vm.StepSyncOnly(cancellationToken);
			}
			CheckImplicitResult(startImpResultCount);
		}

		/// <summary>
		/// Run one VM step without allowing asynchronous waits.  If the current op
		/// needs to await, this method throws.
		/// </summary>
		public void StepSyncOnly(CancellationToken cancellationToken=default) {
			Compile();
			cancellationToken.ThrowIfCancellationRequested();
			vm.StepSyncOnly(cancellationToken);
		}

		/// <summary>
		/// Read Eval Print Loop.  Run the given source until it either terminates,
		/// or cancellation is requested.  When it terminates, if we have new
		/// implicit output, print that to the implicitOutput stream.
		/// </summary>
		/// <param name="sourceLine">Source line.</param>
		/// <param name="cancellationToken">cancellation token for stopping execution</param>
		public async Task REPL(string sourceLine, CancellationToken cancellationToken=default) {
			if (parser == null) parser = new Parser();
			if (vm == null) {
				vm = parser.CreateVM(standardOutput);
				vm.interpreter = new WeakReference(this);
			} else if (vm.done && !parser.NeedMoreInput()) {
				// Since the machine and parser are both done, we don't really need the
				// previously-compiled code.  So let's clear it out, as a memory optimization.
				vm.GetTopContext().ClearCodeAndTemps();
				parser.PartialReset();
			}
			if (sourceLine == "#DUMP") {
				vm.DumpTopContext();
				return;
			}
			
			int startImpResultCount = vm.globalContext.implicitResultCounter;

			try {
				if (sourceLine != null) parser.Parse(sourceLine, true);
				if (!parser.NeedMoreInput()) {
					while (!vm.done) {
						cancellationToken.ThrowIfCancellationRequested();
						ValueTask step = vm.Step(cancellationToken);
						if (!step.IsCompletedSuccessfully) {
							await step.ConfigureAwait(false);
						}
					}
					CheckImplicitResult(startImpResultCount);
				}

			} catch (MiniscriptException mse) {
				ReportError(mse);
				// Attempt to recover from an error by jumping to the end of the code.
				Stop(); // was: vm.GetTopContext().JumpToEnd();
			}
		}
		
		/// <summary>
		/// Report whether the virtual machine is still running, that is,
		/// whether it has not yet reached the end of the program code.
		/// </summary>
		/// <returns></returns>
		public bool Running() {
			return vm != null && !vm.done;
		}
		
		/// <summary>
		/// Return whether the parser needs more input, for example because we have
		/// run out of source code in the middle of an "if" block.  This is typically
		/// used with REPL for making an interactive console, so you can change the
		/// prompt when more input is expected.
		/// </summary>
		/// <returns></returns>
		public bool NeedMoreInput() {
			return parser != null && parser.NeedMoreInput();
		}
		
		/// <summary>
		/// Get a value from the global namespace of this interpreter.
		/// </summary>
		/// <param name="varName">name of global variable to get</param>
		/// <returns>Value of the named variable, or null if not found</returns>
		public Value GetGlobalValue(string varName) {
			if (vm == null) return null;
			TAC.Context c = vm.globalContext;
			if (c == null) return null;
			try {
				return c.GetVar(varName);
			} catch (UndefinedIdentifierException) {
				return null;
			}
		}
		
		/// <summary>
		/// Set a value in the global namespace of this interpreter.
		/// </summary>
		/// <param name="varName">name of global variable to set</param>
		/// <param name="value">value to set</param>
		public void SetGlobalValue(string varName, Value value) {
			if (vm != null) vm.globalContext.SetVar(varName, value);
		}
		
		
		/// <summary>
		/// Helper method that checks whether we have a new implicit result, and if
		/// so, invokes the implicitOutput callback (if any).  This is how you can
		/// see the result of an expression in a Read-Eval-Print Loop (REPL).
		/// </summary>
		/// <param name="previousImpResultCount">previous value of implicitResultCounter</param>
		protected void CheckImplicitResult(int previousImpResultCount) {
			if (implicitOutput != null && vm.globalContext.implicitResultCounter > previousImpResultCount) {

				Value result = vm.globalContext.GetVar(ValVar.implicitResult.identifier);
				if (result != null) {
					implicitOutput.Invoke(result.ToString(vm), true);
				}
			}			
		}
		
		/// <summary>
		/// Report a MiniScript error to the user.  The default implementation 
		/// simply invokes errorOutput with the error description.  If you want
		/// to do something different, then make an Interpreter subclass, and
		/// override this method.
		/// </summary>
		/// <param name="mse">exception that was thrown</param>
		protected virtual void ReportError(MiniscriptException mse) {
			errorOutput.Invoke(mse.Description(), true);
		}
	}
}
