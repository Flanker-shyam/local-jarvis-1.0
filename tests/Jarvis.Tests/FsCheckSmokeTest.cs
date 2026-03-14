using FsCheck;
using FsCheck.Xunit;

namespace Jarvis.Tests;

public class FsCheckSmokeTest
{
    [Property]
    public bool FsCheck_IsWiredUp(int x)
    {
        // Simple property: adding zero is identity
        return x + 0 == x;
    }
}
