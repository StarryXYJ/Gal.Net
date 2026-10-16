namespace GalNet.Editor.Abstraction.Services;

public enum ProjectCloseChoice { Cancel, Save, Discard }

public interface IProjectClosePrompt
{
    Task<ProjectCloseChoice> ChooseAsync(string projectName);
}

public interface IProjectEditLifetime
{
    Task SuspendAsync();
    void ResumeEditing();
}
