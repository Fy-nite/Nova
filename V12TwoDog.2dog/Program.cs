using Engine = twodog.Engine;
using System;
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("V12TwoDog starting...");

        using var engine = new Engine("V12TwoDog", Engine.ResolveProjectDir());
        using var godot = engine.Start();
        
        while (!godot.Iteration())
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                break;
            Update(godot,engine);
        }

        Console.WriteLine("Shutting down...");
    }
    public static void Update(Godot.GodotInstance godot, twodog.Engine engine)
    {
        // Update logic here
        
    }
}
