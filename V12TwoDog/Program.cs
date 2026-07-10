using Godot;
using V12.Core;
using Engine = twodog.Engine;
// i got that dawg in me yo
public class Program
{
    public static void Main(string[] args)
    {

        // Create and start the Godot engine with your project
        using var engine = new Engine("V12TwoDog", Engine.ResolveProjectDir());
        using var godot = engine.Start();
        V12.Core.Networking.BsonConfig.Initialize();

        var root = new GameRoot();
        var renderer = new V12TwoDog.Renderer(engine.Tree);
        //root.Registry.Register("IRenderer", renderer);
        root.Initialize();

        engine.Tree.Root.AddChild(new Node3D());
        GD.Print("2dog is running! Close window or press 'Q' to quit.");
        Console.WriteLine("Press 'Q' to quit.");
        // Main game loop - runs until window closes or 'Q' is pressed
        var lastTime = Time.GetTicksUsec();

        var cam = new Camera3D();

        engine.Tree.Root.AddChild(cam);

        // start the godot main loop
        while (!godot.Iteration())
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                break;

            // is this working?
            if (Input.IsActionJustPressed("summon_cube"))
            {
                var box = new CsgBox3D();
                box.Transform = cam.GlobalTransform;
                box.Position += Vector3.Forward * 2;
                engine.Tree.Root.AddChild(box);
                GD.Print("Added a box!");
            }

            var currentTime = Time.GetTicksUsec();
            var dt = (currentTime - lastTime) / 1000000.0f;
            lastTime = currentTime;
            root.Update(dt);
            var renderables = root.GetAllRenderables();
            foreach (var r in renderables)
            {
                renderer.QueueItem(r);
            }
            renderer.step();
        }

        Console.WriteLine("Shutting down...");
    }

}
