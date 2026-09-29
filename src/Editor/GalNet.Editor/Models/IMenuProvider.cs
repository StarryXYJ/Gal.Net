using System.Collections.Generic;

namespace GalNet.Editor.Models;

/// <summary>
/// 提供菜单数据的接口 —— 任何页面 ViewModel 实现此接口即可提供菜单项，
/// MainWindow 会在导航时自动读取并绑定到 SideMenu 控件。
/// </summary>
public interface IMenuProvider
{
    /// <summary>菜单项集合</summary>
    IList<MenuData> MenuItems { get; }
}
