namespace Fixture.Consumer;

// Triggers APB0003: Resize implements IResizable.Resize but renames its parameter. This is the
// package-level proof of the APB0003 config line — the unit tests exercise the analyzer, only
// this proves the rule is enumerated back on beneath the blanket and reaches a consumer.
public class Panel : IResizable
{
    public void Resize(int size)
    {
    }
}
