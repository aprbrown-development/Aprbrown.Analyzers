using System.Runtime.Versioning;

namespace Fixture.Consumer;

// The preview-annotated API Furnace.Prime calls, which is what makes CA2252 fire over there. It
// lives in its own file only because MA0048 wants a file per type; nothing here is under test.
[RequiresPreviewFeatures]
public static class Prototype
{
    public static void Ignite()
    {
    }
}
