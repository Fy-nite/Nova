using Godot;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12TwoDog;

public partial class RootLoop : Node3D
{
	// Called when the node enters the scene tree for the first time.
	GameRoot root;
	IRenderer renderer;
	public override void _Ready()
	{
		V12.Core.Networking.BsonConfig.Initialize();
		root = new GameRoot();
		 renderer = new V12TwoDog.Renderer(GetTree());
		root.Registry.Register("IRenderer", renderer);
		root.Initialize();
		//root.V12Loop();
		root.CreateWorld("TestWorld","Gridspace");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		root.Update((float)delta);


		foreach (var service in root.Registry.GetAll<IGameService>())
			service.Update((float)delta);

		List<IRenderable> renderables = root.GetAllRenderables();
		
			foreach (var r in renderables)
			{
				renderer.QueueItem(r);
			}
		
		renderer.step();

	}
}
