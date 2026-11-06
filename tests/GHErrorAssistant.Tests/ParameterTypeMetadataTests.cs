using System;
using GHErrorAssistant;
using Xunit;

namespace Grasshopper.Kernel.Types
{
    internal class GH_Curve { }
    internal class GH_Point { }
    internal class GH_Surface { }
    internal class GH_Mesh { }
    internal class GH_Brep { }
    internal interface IGH_Goo { }
}

public class ParameterTypeMetadataTests
{
    [Theory]
    [InlineData(typeof(Grasshopper.Kernel.Types.GH_Curve), "Curve")]
    [InlineData(typeof(Grasshopper.Kernel.Types.GH_Point), "Point")]
    [InlineData(typeof(Grasshopper.Kernel.Types.GH_Surface), "Surface")]
    [InlineData(typeof(Grasshopper.Kernel.Types.GH_Mesh), "Mesh")]
    [InlineData(typeof(Grasshopper.Kernel.Types.GH_Brep), "Brep")]
    [InlineData(typeof(Grasshopper.Kernel.Types.IGH_Goo), "")]
    [InlineData(typeof(string), "")]
    public void MapsOnlyKnownConcreteSdkTypes(Type type, string expected) =>
        Assert.Equal(expected, ParameterTypeMetadata.Describe(() => type));

    [Fact]
    public void FailingOrMissingDescriptorIsUnknown() {
        Assert.Equal("", ParameterTypeMetadata.Describe(() => throw new InvalidOperationException()));
        Assert.Equal("", ParameterTypeMetadata.Describe(() => null));
    }
}

