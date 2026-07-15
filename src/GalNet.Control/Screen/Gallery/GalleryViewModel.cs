using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Gallery;
using GalNet.Core.Services;
using GalNet.Core.UI;

namespace GalNet.Control.ViewModels;
public sealed partial class GalleryItemViewModel : ObservableObject { [ObservableProperty] private string _title=""; public IRelayCommand? OpenCommand {get;init;} }
public sealed partial class GalleryViewModel : ObservableObject
{
 private readonly IGameScreenNavigator _navigator; private readonly IGameProgressService? _progress; private readonly GameFlowOptions _options; public GalleryUiConfiguration Configuration{get;}
 public ObservableCollection<GalleryItemViewModel> Items{get;}=[];
 public GalleryViewModel(IGameScreenNavigator nav,GameFlowOptions options,IGameProgressService? progress,GalleryUiConfiguration config){_navigator=nav;_options=options;_progress=progress;Configuration=config; Refresh();}
 private void Refresh(){foreach(var item in _options.GalleryConfiguration?.Items.Where(x=>_progress?.IsGalleryUnlocked(x.Category,x.SequenceId)??false)??[]){var copy=item;Items.Add(new GalleryItemViewModel{Title=copy.Title??$"{copy.Category} {copy.SequenceId+1}",OpenCommand=new RelayCommand(()=>Open(copy))});}}
 private void Open(GalleryItem item){if(item.Category==GalleryCategory.Scene)_=_navigator.NavigateAsync("game",item.ResourceId);} [RelayCommand] private Task BackAsync()=>_navigator.GoBackAsync();
}
