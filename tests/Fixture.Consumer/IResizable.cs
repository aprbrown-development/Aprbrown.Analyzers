namespace Fixture.Consumer;

// The interface half of the APB0003 case; Panel is the implementer that renames the parameter.
//
// `public` on an interface member is not redundant here even though C# infers it: the shipped
// config sets dotnet_style_require_accessibility_modifiers to `always` rather than
// `for_non_interface_members`, so IDE0040 holds an interface member to the same rule as any other.
// Lathe.cs carries the deliberate violation of that; this file is not it.
public interface IResizable
{
    public void Resize(int width);
}
