using Godot;
using System.Collections.Generic;
using V12.Core;
using V12.Core.NetworkCable;
using V12.Core.Networking;

namespace V12TwoDog
{
    public class NetworkInspector
    {
        private readonly GameRoot _root;
        private string _host = "mc.finite.ovh";
        private int _port = 7777;
        private bool _showDemo;
        private bool _showPacketLog;
        private readonly List<string> _packetLog = new();
        private const int MaxPacketLog = 100;

        public NetworkInspector(GameRoot root)
        {
            _root = root;
            _root.Cables.OnMessageReceived += LogPacket;
        }

        private void LogPacket(MessageDTO msg)
        {
            _packetLog.Add($"[{msg.MessageType}] from {msg.Sender}");
            if (_packetLog.Count > MaxPacketLog)
                _packetLog.RemoveAt(0);
        }

        public void OnLayout()
        {
            if (ImGui.Begin("Network Inspector"))
            {
                var client = _root.Registry.Get<NetworkClient>("NetworkClient");
                var host = _root.Registry.Get<NetworkHost>("NetworkHost");

                if (client != null)
                {
                    ImGui.TextColored(new Color(1, 1, 0), $"Client: {client.Host}:{client.Port}");

                    bool connected = client.IsConnected;
                    if (connected)
                        ImGui.TextColored(new Color(0, 1, 0), "Status: Connected");
                    else
                        ImGui.TextColored(new Color(1, 0, 0), "Status: Disconnected");

                    ImGui.Separator();
                    ImGui.Text("Connect to:");
                    _host = ImGui.InputText("Host", _host, 256);
                    _port = ImGui.InputInt("Port", _port);

                    if (connected)
                    {
                        if (ImGui.Button("Disconnect"))
                            client.Disconnect();
                    }
                    else
                    {
                        if (ImGui.Button("Connect"))
                        {
                            // Use the existing client created by SetupNetworking,
                            // but connect to the host/port specified in the UI fields.
                            _ = client.ConnectAsync(_host, _port);
                        }
                    }
                }
                else if (host != null)
                {
                    ImGui.TextColored(new Color(0, 1, 1), $"Server on port {host.Port}");

                    if (host.IsRunning)
                        ImGui.TextColored(new Color(0, 1, 0), "Status: Running");
                    else
                        ImGui.TextColored(new Color(1, 0, 0), "Status: Stopped");

                    ImGui.Text($"Clients: {host.ClientCount}");

                    if (host.IsRunning)
                    {
                        if (ImGui.Button("Stop Server"))
                            host.Stop();
                    }
                    else
                    {
                        if (ImGui.Button("Start Server"))
                            _ = host.StartAsync();
                    }
                }
                else
                {
                    ImGui.Text("No networking configured");
                }

                ImGui.Separator();
                var selected = _root.SelectedWorld;
                if (selected != null)
                {
                    ImGui.Text($"World: {selected.WorldName}");
                    ImGui.Text($"Root elements: {selected.Root.Count}");
                }
                else
                {
                    ImGui.Text("No world selected");
                }

                ImGui.Separator();
                var cables = _root.Registry.Get<NetworkCables>("NetworkCables");
                if (cables != null)
                {
                    ImGui.Text($"Send queue: {cables.SendQueue.Count}");
                    ImGui.Text($"Recv queue: {cables.ReceiveQueue.Count}");
                }

                var tracker = _root.Registry.Get<DirtyTracker>("DirtyTracker");
                if (tracker != null)
                {
                    ImGui.Text($"Dirty components: {tracker.PendingComponentCount}");
                    ImGui.Text($"Dirty elements: {tracker.PendingElementCount}");
                }

                ImGui.Separator();
                if (ImGui.Checkbox("Show packet log", _showPacketLog) != _showPacketLog)
                    _showPacketLog = !_showPacketLog;
                if (_showPacketLog)
                {
                    if (ImGui.BeginChild("PacketLog", 0, 200))
                    {
                        foreach (var entry in _packetLog)
                            ImGui.Text(entry);
                    }
                    ImGui.EndChild();
                }

                ImGui.Separator();
                if (ImGui.Checkbox("Show ImGui Demo", _showDemo) != _showDemo)
                    _showDemo = !_showDemo;
                if (_showDemo)
                    ImGui.ShowDemoWindow();
            }
            ImGui.End();
        }
    }
}
