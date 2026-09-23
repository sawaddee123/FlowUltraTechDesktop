using System;
using System.Collections;
using System.Reflection;
using System.Resources;
using System.Windows.Markup;
using Xunit;
using Xunit.Abstractions;

namespace FlowUltraTech.Tests;

public class WpfUiReflectionTests
{
    private readonly ITestOutputHelper _output;

    public WpfUiReflectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectWpfUiAssembly()
    {
        var asm = typeof(Wpf.Ui.Controls.FluentWindow).Assembly;
        _output.WriteLine($"Assembly: {asm.FullName}");

        foreach (var attr in asm.GetCustomAttributes<XmlnsDefinitionAttribute>())
        {
            _output.WriteLine($"XmlnsDefinition: {attr.XmlNamespace} -> {attr.ClrNamespace}");
        }

        var stream = asm.GetManifestResourceStream("Wpf.Ui.g.resources");
        if (stream != null)
        {
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                _output.WriteLine($"Baml/Resource: {entry.Key}");
            }
        }
    }
}
