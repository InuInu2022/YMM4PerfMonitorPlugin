using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace YMM4PerfMonitorPlugin.View;

public partial class PerfGraph : UserControl
{
	private readonly Dictionary<string, Queue<double>> _series = new();

	private readonly int _maxPoints = 100;

	public void AddValue(string series, double value)
	{
		if (!_series.TryGetValue(series, out var q))
		{
			q = new Queue<double>(_maxPoints);
			_series[series] = q;
		}

		if (q.Count >= _maxPoints)
			q.Dequeue();

		q.Enqueue(value);
		InvalidateVisual(); // 再描画要求
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);

		var width = ActualWidth;

		var height = ActualHeight;

		if (width <= 0 || height <= 0)
			return;

		// 軸線（オプション）
		dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

		int colorIndex = 0;

		foreach (var kv in _series)
		{
			var brush = new[] { Brushes.Lime, Brushes.Cyan, Brushes.Red, Brushes.Yellow }[
				colorIndex % 4
			];

			var pen = new Pen(brush, 1);
			var values = kv.Value.ToArray();

			if (values.Length < 2)
				continue;

			double stepX = width / (double)_maxPoints;
			double maxY = values.Max() * 1.1; // スケーリング用
			var prev = new Point(0, height - (values[0] / maxY * height));

			for (int i = 1; i < values.Length; i++)
			{
				var cur = new Point(i * stepX, height - (values[i] / maxY * height));
				dc.DrawLine(pen, prev, cur);
				prev = cur;
			}
			colorIndex++;
		}
	}
}
