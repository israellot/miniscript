using BenchmarkDotNet.Attributes;
using Miniscript;
using System.Threading.Tasks;

namespace MiniScript.Benchmarks;

[MemoryDiagnoser]
public class InterpreterBenchmarks
{
	private const string CompileScript = """
		fib = function(n)
			if n <= 1 then return n
			return fib(n - 1) + fib(n - 2)
		end function
		""";

	private const string ExecuteScript = """
		fib = function(n)
			if n <= 1 then return n
			return fib(n - 1) + fib(n - 2)
		end function

		result = fib(12)
		""";

	private Interpreter precompiledInterpreter = null!;

	[GlobalSetup(Target = nameof(Run_Precompiled))]
	public void SetupPrecompiled()
	{
		precompiledInterpreter = CreateInterpreter(ExecuteScript);
		precompiledInterpreter.Compile();
	}

	[Benchmark]
	public void Compile_RecursiveFunction()
	{
		var interpreter = CreateInterpreter(CompileScript);
		interpreter.Compile();
	}

	[Benchmark]
	public async Task Run_CompileAndExecute()
	{
		var interpreter = CreateInterpreter(ExecuteScript);
		await interpreter.RunUntilDone().ConfigureAwait(false);
	}

	[Benchmark]
	public async Task Run_Precompiled()
	{
		precompiledInterpreter.Restart();
		await precompiledInterpreter.RunUntilDone().ConfigureAwait(false);
	}

	private static Interpreter CreateInterpreter(string source)
	{
		var interpreter = new Interpreter(source);
		interpreter.standardOutput = static (_, _) => { };
		interpreter.errorOutput = static (_, _) => { };
		interpreter.implicitOutput = static (_, _) => { };
		return interpreter;
	}
}
