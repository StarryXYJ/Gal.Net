using GalNet.Core.Runtime;

namespace GalNet.Runtime.Runtime;

/// <summary>Engine-owned cursor and restoration operations, excluded from primitive contexts.</summary>
public interface IGameRuntime : IGameContext
{
    void JumpTo(string nodeId, int entryIndex = 0);
    void SetEntryIndex(int entryIndex);
    GameSnapshot CreateSnapshot();
    GameRestorePoint PrepareRestore(GameSnapshot snapshot);
    void Restore(GameRestorePoint point);
}
