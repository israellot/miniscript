using BenchmarkDotNet.Attributes;
using Miniscript;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniScript.Benchmarks;

[MemoryDiagnoser]
public class LambdaIntrinsicBenchmarks
{
	private const string AddManualName = "__bench_add_manual";
	private const string AddLambdaName = "__bench_add_lambda";
	private const string ListManualName = "__bench_list_manual";
	private const string ListLambdaName = "__bench_list_lambda";

	private const string PrimitiveManualScript = """
		i = 0
		sum = 0
		while i < 500
			sum += __bench_add_manual(i, 2)
			i += 1
		end while
		""";

	private const string PrimitiveLambdaScript = """
		i = 0
		sum = 0
		while i < 500
			sum += __bench_add_lambda(i, 2)
			i += 1
		end while
		""";

	private const string ListManualScript = """
		nums = [1, 2, 3, 4, 5, 6, 7, 8]
		i = 0
		sum = 0
		while i < 300
			sum += __bench_list_manual(nums)
			i += 1
		end while
		""";

	private const string ListLambdaScript = """
		nums = [1, 2, 3, 4, 5, 6, 7, 8]
		i = 0
		sum = 0
		while i < 300
			sum += __bench_list_lambda(nums)
			i += 1
		end while
		""";

	private Interpreter primitiveManualInterpreter = null!;
	private Interpreter primitiveLambdaInterpreter = null!;
	private Interpreter listManualInterpreter = null!;
	private Interpreter listLambdaInterpreter = null!;

	[GlobalSetup]
	public void Setup()
	{
		EnsureIntrinsics();
		primitiveManualInterpreter = CreatePrecompiledInterpreter(PrimitiveManualScript);
		primitiveLambdaInterpreter = CreatePrecompiledInterpreter(PrimitiveLambdaScript);
		listManualInterpreter = CreatePrecompiledInterpreter(ListManualScript);
		listLambdaInterpreter = CreatePrecompiledInterpreter(ListLambdaScript);
	}

	[Benchmark(Baseline = true)]
	public async Task Run_ManualPrimitive_Precompiled()
	{
		primitiveManualInterpreter.Restart();
		await primitiveManualInterpreter.RunUntilDone().ConfigureAwait(false);
	}

	[Benchmark]
	public async Task Run_LambdaPrimitive_Precompiled()
	{
		primitiveLambdaInterpreter.Restart();
		await primitiveLambdaInterpreter.RunUntilDone().ConfigureAwait(false);
	}

	[Benchmark]
	public async Task Run_ManualList_Precompiled()
	{
		listManualInterpreter.Restart();
		await listManualInterpreter.RunUntilDone().ConfigureAwait(false);
	}

	[Benchmark]
	public async Task Run_LambdaList_Precompiled()
	{
		listLambdaInterpreter.Restart();
		await listLambdaInterpreter.RunUntilDone().ConfigureAwait(false);
	}

	private static void EnsureIntrinsics()
	{
		if (Intrinsic.GetByName(AddManualName) == null)
		{
			var manual = Intrinsic.Create(AddManualName);
			manual.AddParam("x", 0);
			manual.AddParam("y", 0);
			manual.code = static (context) =>
			{
				double x = context.GetLocalDouble("x");
				double y = context.GetLocalDouble("y");
				return new Intrinsic.Result(x + y);
			};
		}

		if (Intrinsic.GetByName(AddLambdaName) == null)
		{
			Intrinsic.CreateFromLambda<Func<double, double, double>>(AddLambdaName, static (x, y) => x + y);
		}

		if (Intrinsic.GetByName(ListManualName) == null)
		{
			var manualList = Intrinsic.Create(ListManualName);
			manualList.AddParam("values");
			manualList.code = static (context) =>
			{
				if (context.GetLocal("values") is not ValList list) return new Intrinsic.Result(0.0);
				double sum = 0;
				for (int i = 0; i < list.values.Count; i++)
				{
					Value value = list.values[i];
					sum += value == null ? 0 : value.DoubleValue();
				}
				return new Intrinsic.Result(sum);
			};
		}

		if (Intrinsic.GetByName(ListLambdaName) == null)
		{
			Intrinsic.CreateFromLambda<Func<List<double>, double>>(ListLambdaName, SumList);
		}
	}

	private static double SumList(List<double> values)
	{
		if (values == null) return 0;
		double sum = 0;
		for (int i = 0; i < values.Count; i++) sum += values[i];
		return sum;
	}

	private static Interpreter CreatePrecompiledInterpreter(string source)
	{
		var interpreter = new Interpreter(source);
		interpreter.standardOutput = static (_, _) => { };
		interpreter.errorOutput = static (_, _) => { };
		interpreter.implicitOutput = static (_, _) => { };
		interpreter.Compile();
		return interpreter;
	}
}
