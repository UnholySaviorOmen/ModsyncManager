using FluentAssertions;
using Modsync.Core.Nxm;

namespace Modsync.Core.Tests.Nxm;

public class NxmPipeNameTests
{
    [Fact]
    public void Value_IsExpectedConstant()
    {
        // Значение — часть публичного контракта между handler-ом
        // и receiver-ом. Изменение — breaking change.
        NxmPipeName.Value.Should().Be("modsyncmanager-nxm");
    }

    [Fact]
    public void Value_IsNotEmpty()
    {
        NxmPipeName.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Value_ContainsNoBackslashes()
    {
        // Префикс \\.\pipe\ добавляется BCL, не нами.
        NxmPipeName.Value.Should().NotContain("\\");
        NxmPipeName.Value.Should().NotContain("/");
    }
}
