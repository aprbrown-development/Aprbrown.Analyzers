namespace Fixture.Consumer;

// The interface half of the APB0003 case; Panel is the implementer that renames the parameter.
public interface IResizable
{
    void Resize(int width);
}
