using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Epoxy;
using System.Management; // ファイル冒頭に追加

using YMM4PerfMonitorPlugin.View;

using static System.Net.Mime.MediaTypeNames;
using System.Globalization;

namespace YMM4PerfMonitorPlugin.ViewModel;

[ViewModel]
public class PerfMonitorViewModel : IDisposable
{
	// プロパティ
	public double FPS { get; private set; }
	public double CPU { get; private set; }
	public long Memory { get; private set; }
	public string GCInfo { get; private set; } = "";
	public int ThreadCount { get; private set; }
	public double DispatcherDelay { get; private set; }


	public PointCollection FPSPoints
	{
		get; private set;
	} = [];

	public PointCollection CpuPoints
	{
		get; private set;
	} = [];

	public PointCollection MemoryPoints
	{
		get; private set;
	} = [];

	private readonly List<double> _cpuHistory  = [..Enumerable.Range(0, MaxPoints)];
	private readonly List<double> _memoryHistory = [..Enumerable.Range(0, MaxPoints)];


	// FPS履歴用リスト
	private readonly List<double> _fpsHistory
		= [..Enumerable.Range(0, MaxPoints)];

	public Well<UserControl> MainWell =>
		Well.Factory.Create<UserControl>();
	public Well<Canvas> CanvasWell =>
		Well.Factory.Create<Canvas>();
	// Canvasサイズ
	public double CanvasWidth { get; set; } = 300;
	public double CanvasHeight { get; set; } = 100;

	// FPS計測
	private DateTime _lastFpsUpdate = DateTime.UtcNow;
	private int _frameCount;

	private readonly DispatcherTimer _timer;
	private readonly Process _proc = Process.GetCurrentProcess();

	const int MaxPoints = 100;

	private bool _disposedValue;

	private readonly EventHandler _renderingHandler;

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Concurrency", "PH_S030:Async Void Method Invocation", Justification = "<保留中>")]
	public PerfMonitorViewModel()
	{
		// FPS計測フック
		_renderingHandler = (sender, e) =>
		{
			_frameCount++;
		};
		CompositionTarget.Rendering += _renderingHandler;

		// 1秒ごとに更新
		_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		_timer.Tick += (_, __) => UpdateAsyncSafe();
		_timer.Start();


		MainWell.Add("Loaded", () =>
		{
			CanvasWell.Add<SizeChangedEventArgs>(FrameworkElement.SizeChangedEvent, (e) =>
			{
				OnCanvasSizeChanged(this, e);
				return default;
			});

			return default;
		});



	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Correctness", "SS001:Async methods should return a Task to make them awaitable", Justification = "<保留中>")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "<保留中>")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<保留中>")]
	async void UpdateAsyncSafe()
	{
		try
		{
			await UpdateAsync().ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			Debug.WriteLine(ex);
		}
	}



	[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD001:Avoid legacy thread switching APIs", Justification = "<保留中>")]
	private async Task UpdateAsync()
	{
		// FPS計算: 1秒間のフレーム数
		var now = DateTime.UtcNow;
		var elapsed = (now - _lastFpsUpdate).TotalSeconds;
		if (elapsed > 0)
		{
			FPS = _frameCount / elapsed;
		}
		else
		{
			FPS = 0;
		}
		_lastFpsUpdate = now;
		_frameCount = 0;

		_proc.Refresh();

		CPU = GetCpuUsage();

		Memory = (long)((double)_proc.WorkingSet64 / (1024 * 1024));

		GCInfo =
			$"GC0/1/2: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}";
		ThreadCount = _proc.Threads.Count;

		// Dispatcher遅延測定
		var sw = Stopwatch.StartNew();
		_ = System.Windows.Application.Current.Dispatcher.BeginInvoke(
			DispatcherPriority.Background,
			new Action(() => DispatcherDelay = sw.ElapsedMilliseconds)
		);

		if (_fpsHistory.Count >= MaxPoints)
		{
			_fpsHistory.RemoveAt(0);
		}
		_fpsHistory.Add(FPS);
		if (_cpuHistory.Count >= MaxPoints) _cpuHistory.RemoveAt(0);
		_cpuHistory.Add(CPU);

		if (_memoryHistory.Count >= MaxPoints) _memoryHistory.RemoveAt(0);
		_memoryHistory.Add(Memory);

		RegenerateFpsPoints();
		RegenerateCpuPoints();
		RegenerateMemoryPoints();
	}



	// ViewからCanvasのサイズ変更イベントを受け取る
	public void OnCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
	{
		if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
		{
			CanvasWidth = e.NewSize.Width;
			CanvasHeight = e.NewSize.Height;
			// サイズ変更時もグラフ再生成
			RegenerateFpsPoints();
		}
	}

	// FPSグラフ: FPSが低いほどグラフが上に跳ね上がる（高負荷を直感的に表示）
	void RegenerateFpsPoints()
	{
		if (_fpsHistory.Count <= 1)
			return;

		double width = CanvasWidth;
		double height = CanvasHeight;
		double maxFps = _fpsHistory.Max() * 1.1;
		if (maxFps == 0)
			maxFps = 1;

		var points = new PointCollection();
		int count = _fpsHistory.Count;
		for (int i = 0; i < count; i++)
		{
			double x = (count == 1) ? 0 : i * width / (count - 1);
			// FPSが低いほどYが大きく（上に）なる
			double y = height * (1.0 - (_fpsHistory[i] / maxFps));
			points.Add(new Point(x, y));
		}
		FPSPoints = points;
	}

	void RegenerateCpuPoints()
	{
		if (_cpuHistory.Count <= 1) return;
		double width = CanvasWidth;
		double height = CanvasHeight;
		const double max = 100.0; // 常に100%スケール
		var points = new PointCollection();
		int count = _cpuHistory.Count;
		for (int i = 0; i < count; i++)
		{
			double x = (count == 1) ? 0 : i * width / (count - 1);
			double y = height * (1.0 - (_cpuHistory[i] / max));
			points.Add(new Point(x, y));
		}
		CpuPoints = points;
	}

	long? totalPhysicalMemoryMB;

	long GetTotalPhysicalMemoryMB()
	{
		if (totalPhysicalMemoryMB is not null)
		{
			return totalPhysicalMemoryMB.Value;
		}
		try
		{
			using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
			foreach (var obj in searcher.Get())
			{
				if (obj["TotalPhysicalMemory"] is string memStr && long.TryParse(memStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var bytes))
				{
					totalPhysicalMemoryMB = (long)Math.Round((double)bytes / (1024 * 1024));
					return totalPhysicalMemoryMB.Value;
				}
			}
		}
		catch
		{
			// ignore and fallback
			totalPhysicalMemoryMB = 4096; // 取得失敗時は4GB
		}
		totalPhysicalMemoryMB = 4096; // 取得失敗時は4GB
		return totalPhysicalMemoryMB.Value;
	}

	void RegenerateMemoryPoints()
	{
		if (_memoryHistory.Count <= 1) return;
		double width = CanvasWidth;
		double height = CanvasHeight;
		double max = GetTotalPhysicalMemoryMB();
		if (max == 0) max = 1;
		var points = new PointCollection();
		int count = _memoryHistory.Count;
		for (int i = 0; i < count; i++)
		{
			double x = (count == 1) ? 0 : i * width / (count - 1);
			double y = height * (1.0 - (_memoryHistory[i] / max));
			points.Add(new Point(x, y));
		}
		MemoryPoints = points;
	}

	// 前回値を保持するフィールドを用意
	private TimeSpan _lastTotalProcessorTime;
	private DateTime _lastCpuCheckTime;

	double GetCpuUsage()
	{
		var now = DateTime.UtcNow;
		var totalProcTime = _proc.TotalProcessorTime;

		if (_lastCpuCheckTime == default)
		{
			_lastCpuCheckTime = now;
			_lastTotalProcessorTime = totalProcTime;
			return 0;
		}

		var cpuUsedMs = (totalProcTime - _lastTotalProcessorTime).TotalMilliseconds;
		var elapsedMs = (now - _lastCpuCheckTime).TotalMilliseconds;
		_lastCpuCheckTime = now;
		_lastTotalProcessorTime = totalProcTime;

		if (elapsedMs <= 0) return 0;
		return cpuUsedMs / (elapsedMs * Environment.ProcessorCount) * 100.0;
	}

	[PropertyChanged(nameof(FPSPoints))]
	[SuppressMessage("","IDE0051")]
	private ValueTask FPSPointsChangedAsync(PointCollection value)
	{
		return default;
	}

	[PropertyChanged(nameof(CpuPoints))]
	[SuppressMessage("", "IDE0051")]
	private ValueTask CpuPointsChangedAsync(PointCollection value)
	{
		return default;
	}

	[PropertyChanged(nameof(MemoryPoints))]
	[SuppressMessage("", "IDE0051")]
	private ValueTask MemoryPointsChangedAsync(PointCollection value)
	{
		return default;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				_timer.Stop();
				CompositionTarget.Rendering -= _renderingHandler;
				_proc.Dispose();
				_timer.Stop();
			}

			_disposedValue = true;
		}
	}

	// // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
	// ~PerfMonitorViewModel()
	// {
	//     // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
	//     Dispose(disposing: false);
	// }

	public void Dispose()
	{
		// このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
