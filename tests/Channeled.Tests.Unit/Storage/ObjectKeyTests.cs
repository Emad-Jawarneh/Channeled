using Channeled.Domain.Storage;
using FluentAssertions;

namespace Channeled.Tests.Unit.Storage;

public class ObjectKeyTests
{
    [Fact]
    public void ForFile_uses_opaque_hex_prefix_and_uuid()
    {
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        var key = ObjectKey.ForFile(id);

        key.Should().Be("objects/01/23/01234567-89ab-cdef-0123-456789abcdef");
        key.Should().NotContain(".");
        key.Should().NotContain(" ");
    }
}
