using System;

namespace AyaHitmarker.LogicTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("AyaHitmarker offline logic tests");
        Console.WriteLine("(compiles Game/KillStreak.cs + Ui/KillCardLayout.cs directly — no Unity, no game assemblies)");

        KillStreakFacts.Run();
        BodyPartFacts.Run();
        KillCardLayoutFacts.Run();

        return Assert.Summary();
    }
}
