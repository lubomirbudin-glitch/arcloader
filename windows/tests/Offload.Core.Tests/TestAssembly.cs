using System.Runtime.CompilerServices;

namespace Offload.Core.Tests;

/// Fix round 2, item 9: a job in a test prints no PDF (no headless Edge per job — slow, and it flaked under load);
/// a test that checks the PDF asks for it with `using var _ = PdfReport.PrintHere();` and sits in the Edge collection.
static class TestAssembly
{
#pragma warning disable CA2255 // a module initializer in a test assembly is exactly the use it is meant for
    [ModuleInitializer]
    internal static void SkipPdfs() => PdfReport.SkipUnlessAsked = true;
#pragma warning restore CA2255

    /// What a test waits for a job that may print a PDF: longer than Edge may take, with room to spare.
    public static readonly TimeSpan JobWait = TimeSpan.FromSeconds(PdfReport.DefaultTimeoutSeconds + 60);
}

/// Every test that launches Edge: one at a time, after the parallel tests.
[CollectionDefinition("Edge", DisableParallelization = true)]
public class EdgeCollection;
