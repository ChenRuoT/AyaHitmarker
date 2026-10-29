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

        // dotnet run --project tests/AyaHitmarker.LogicTests -- --render-hitdir <目录>
        // 受击方向点阵的离线预览（见 HitDirectionPreview.cs）
        if (args.Length >= 2 && args[0] == "--render-hitdir")
        {
            HitDirectionPreview.Render(args[1]);
            return 0;
        }

        Console.WriteLine("AyaHitmarker offline logic tests");
        Console.WriteLine("(compiles the pure-logic files directly — no Unity, no game assemblies)");

        DiagClockFacts.Run();
        KillExperienceFacts.Run();
        AudioMetricsFacts.Run();
        KillStreakFacts.Run();
        BodyPartFacts.Run();
        KillCardLayoutFacts.Run();
        KillCardModelFacts.Run();
        DamageAccumFacts.Run();
        GlitchFacts.Run();
        IconShapeFacts.Run();
        AudioClipHealthFacts.Run();
        ConfigPresentationFacts.Run();
        HitDirectionFacts.Run();
        AssistFacts.Run();
        ArmorHitMergerFacts.Run();
        HeadshotFacts.Run();
        LocFacts.Run();
        NameTranslitFacts.Run();
        ColorVisionFacts.Run();
        RaidStatsFacts.Run();
        PresetFacts.Run();
        ConfigTextFacts.Run();

        return Assert.Summary();
    }
}
