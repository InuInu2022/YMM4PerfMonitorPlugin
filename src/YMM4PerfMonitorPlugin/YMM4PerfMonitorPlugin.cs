
using YukkuriMovieMaker.Plugin;
using YMM4PerfMonitorPlugin.View;
using YMM4PerfMonitorPlugin.ViewModel;
using System.Reflection;

namespace YMM4PerfMonitorPlugin;

[PluginDetails(AuthorName = "InuInu", ContentId = "")]
public class YMM4PerfMonitorPlugin : IToolPlugin
{
	public Type ViewModelType => typeof(PerfMonitorViewModel);
	public Type ViewType => typeof(PerfMonitorView);
	public string Name => "YMM4 Performance Monitor";

	public PluginDetailsAttribute Details =>
		GetType()
			.GetCustomAttribute<PluginDetailsAttribute>()
		?? new();

}
