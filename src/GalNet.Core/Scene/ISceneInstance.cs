namespace GalNet.Core.Scene;

/// <summary>A currently active scene object addressed by an opaque runtime handle.</summary>
public interface ISceneInstance
{
    string Id { get; }
}
