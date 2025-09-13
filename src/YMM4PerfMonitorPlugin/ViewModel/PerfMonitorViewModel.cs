using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Epoxy;

using YMM4PerfMonitorPlugin.View;

using static System.Net.Mime.MediaTypeNames;

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

	private PointCollection _fpsPoints = new();
	public PointCollection FPSPoints
	{
		get => _fpsPoints;
		private set
		{
			if (_fpsPoints != value)
			{
				_fpsPoints = value;
				OnPropertyChanged(nameof(FPSPoints));
			}
		}
	}

	// FPS履歴用リスト
	private readonly List<double> _fpsHistory
		= [..Enumerable.Range(0, MaxPoints)];

	public event PropertyChangedEventHandler? PropertyChanged;

	public Well<UserControl> MainWell =>
		Well.Factory.Create<UserControl>();
	public Well<Canvas> CanvasWell =>
		Well.Factory.Create<Canvas>();
	// Canvasサイズ
	public double CanvasWidth { get; set; } = 300;
	public double CanvasHeight { get; set; } = 100;

	// FPS計測
	private DateTime _lastRender = DateTime.UtcNow;
	private int _frameCount;
	private double _fpsAccumulator;

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
			var now = DateTime.UtcNow;
			var delta = (now - _lastRender).TotalSeconds;
			_lastRender = now;

			if (delta > 0)
			{
				_fpsAccumulator += 1.0 / delta;
				_frameCount++;
			}
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
		if (_frameCount > 0)
		{
			FPS = _fpsAccumulator / _frameCount;
			_fpsAccumulator = 0;
			_frameCount = 0;
		}

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
		RegenerateFpsPoints();
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

	// FPSPoints生成処理をメソッド化
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
			// 点間隔を履歴数で均等割り付け
			double x = (count == 1) ? 0 : i * width / (count - 1);
			double y = height - (_fpsHistory[i] / maxFps * height);
			points.Add(new Point(x, y));
		}
		FPSPoints = points;
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

	void OnPropertyChanged(string? name) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
