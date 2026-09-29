using System;
using System.Threading.Tasks;

namespace GalNet.Editor.Services.Interfaces;

/// <summary>Serializes project writes and provides debounced auto-save scheduling.</summary>
public interface IProjectSaveScheduler
{
    void Schedule(Func<Task> save);
    Task SaveNowAsync(Func<Task> save);
}
