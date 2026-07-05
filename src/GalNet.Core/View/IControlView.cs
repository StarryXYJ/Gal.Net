namespace GalNet.Core.View;

public interface IControlView
{
    void ShowControl(string instanceId);
    void HideControl(string instanceId);
    void SetControlProperty(string instanceId, string property, string value);
}
