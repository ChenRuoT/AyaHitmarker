using System;

namespace AyaHitmarker.LogicTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        // dotnet run --project tests/AyaHitmarker.LogicTests -- --render-icons <目录>
        // 把程序化图标渲染成 PNG 联系表，肉眼检查（见 IconPreview.cs）
        if (args.Length >= 2 && args[0] == "--render-icons")
        {
            IconPreview.Render(args[1]);
            return 0;
        }

        Console.WriteLine("AyaHitmarker offline logic tests");
        Console.WriteLine("(compiles the pure-logic files directly — no Unity, no game assemblies)");

        KillStreakFacts.Run();
        BodyPartFacts.Run();
        KillCardLayoutFacts.Run();
        KillCardModelFacts.Run();
        GlitchFacts.Run();
        IconShapeFacts.Run();

        return Assert.Summary();
    }
}
