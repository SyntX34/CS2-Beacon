using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using CounterStrikeSharp.API.Modules.Admin;

namespace BeaconPlugin;

[MinimumApiVersion(96)]
public class BeaconPlugin : BasePlugin, IPluginConfig<BeaconConfig>
{
    public override string ModuleName => "Beacon Plugin";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "+SyntX";
    public override string ModuleDescription => "Toggles a beacon effect around players using css_beacon command";

    public BeaconConfig Config { get; set; } = new();
    private readonly Dictionary<CCSPlayerController, (CounterStrikeSharp.API.Modules.Timers.Timer? Timer, List<CEnvBeam> Beams, bool Active, CounterStrikeSharp.API.Modules.Timers.Timer? SoundTimer, int RemainingSeconds, float PulsePhase)> _playerBeacons = new();
    private string? logFilePath;

    public void OnConfigParsed(BeaconConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        string? cssDirectory = Path.GetDirectoryName(Path.GetDirectoryName(ModuleDirectory));
        logFilePath = cssDirectory != null ? Path.Combine(cssDirectory, "logs", "beacon_logs.txt") : Path.Combine(Directory.GetCurrentDirectory(), "logs", "beacon_logs.txt");
        string? logDirectory = Path.GetDirectoryName(logFilePath);
        if (logDirectory != null && !Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }
        if (logFilePath != null && !File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "");
        }

        Server.PrintToConsole("[BeaconPlugin] Loaded successfully");
        AddCommand("css_beacon", "Places a beacon effect on a target player or team", OnBeaconCommand);
        RegisterListener<Listeners.OnClientDisconnect>(playerSlot =>
        {
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            if (player != null && _playerBeacons.ContainsKey(player))
            {
                StopBeacon(player);
            }
        });

        RegisterEventHandler<EventPlayerDeath>((@event, info) =>
        {
            var player = @event.Userid;
            if (player != null && _playerBeacons.ContainsKey(player))
            {
                StopBeacon(player);
            }
            return HookResult.Continue;
        });
    }

    private void OnBeaconCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || Config.PluginEnabled != 1)
        {
            command.ReplyToCommand("Beacon plugin is disabled or command invalid.");
            return;
        }

        if (!AdminManager.PlayerHasPermissions(player, Config.CommandAccess))
        {
            command.ReplyToCommand("You do not have permission to use this command.");
            return;
        }

        string targetArg = command.ArgString;
        if (string.IsNullOrWhiteSpace(targetArg))
        {
            command.ReplyToCommand("Usage: !beacon <target> (e.g., !beacon @me, !beacon @t, !beacon <playername>)");
            return;
        }

        var targets = command.GetArgTargetResult(0).Where(p => p.PawnIsAlive).ToList();

        if (targets.Count == 0)
        {
            command.ReplyToCommand("No valid target found.");
            return;
        }

        foreach (var targetPlayer in targets)
        {
            if (!_playerBeacons.ContainsKey(targetPlayer))
            {
                _playerBeacons[targetPlayer] = (null, new List<CEnvBeam>(), false, null, 0, 0.0f);
            }

            string adminName = player.PlayerName;
            string adminSteamId = player.SteamID.ToString();
            string clientSteamId = targetPlayer.SteamID.ToString();
            string mapName = Server.MapName;

            if (!_playerBeacons[targetPlayer].Active)
            {
                StartBeacon(targetPlayer);
                targetPlayer.PrintToChat($"{ChatColors.Green}[Beacon]{ChatColors.Default} Admin {adminName} has toggled a beacon on you for {Config.BeaconDuration} seconds!");
                LogEvent($"{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} - Admin {adminName} (SteamID: {adminSteamId}) started beacon on {targetPlayer.PlayerName} (SteamID: {clientSteamId}) on map {mapName}");
            }
            else
            {
                StopBeacon(targetPlayer);
                targetPlayer.PrintToChat($"{ChatColors.Green}[Beacon]{ChatColors.Default} Admin {adminName} has stopped your beacon.");
                LogEvent($"{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} - Admin {adminName} (SteamID: {adminSteamId}) stopped beacon on {targetPlayer.PlayerName} (SteamID: {clientSteamId}) on map {mapName}");
            }
        }
    }

    private void StartBeacon(CCSPlayerController player)
    {
        if (!player.IsValid || !player.PawnIsAlive)
        {
            return;
        }

        StopBeacon(player);

        var beams = new List<CEnvBeam>();
        var timer = AddTimer(0.1f, () => UpdateBeacon(player, beams), TimerFlags.REPEAT);
        var soundTimer = Config.EnableSound == 1 ? AddTimer(1.0f, () => PlayBeaconSound(player), TimerFlags.REPEAT) : null;

        _playerBeacons[player] = (timer, beams, true, soundTimer, Config.BeaconDuration, 0.0f);
    }

    private void StopBeacon(CCSPlayerController player)
    {
        if (!_playerBeacons.ContainsKey(player))
        {
            return;
        }

        var (timer, beams, _, soundTimer, _, _) = _playerBeacons[player];
        if (timer != null)
        {
            timer.Kill();
        }
        if (soundTimer != null)
        {
            soundTimer.Kill();
        }

        foreach (var beam in beams)
        {
            if (beam != null && beam.IsValid)
            {
                beam.Remove();
            }
        }

        _playerBeacons.Remove(player);
    }

    private void UpdateBeacon(CCSPlayerController player, List<CEnvBeam> Beams)
    {
        if (!player.IsValid || !player.PawnIsAlive || !_playerBeacons.ContainsKey(player) || !_playerBeacons[player].Active)
        {
            StopBeacon(player);
            player.PrintToChat($"{ChatColors.Green}[Beacon]{ChatColors.Default} Your beacon has expired.");
            string clientSteamId = player.SteamID.ToString();
            string mapName = Server.MapName;
            LogEvent($"{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} - Beacon expired on {player.PlayerName} (SteamID: {clientSteamId}) on map {mapName}");
            return;
        }

        foreach (var beam in Beams)
        {
            if (beam != null && beam.IsValid)
            {
                beam.Remove();
            }
        }
        Beams.Clear();

        var mid = player.Pawn.Value?.AbsOrigin;
        if (mid == null)
        {
            StopBeacon(player);
            player.PrintToChat($"{ChatColors.Green}[Beacon]{ChatColors.Default} Your beacon has expired.");
            string clientSteamId = player.SteamID.ToString();
            string mapName = Server.MapName;
            LogEvent($"{DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} - Beacon expired on {player.PlayerName} (SteamID: {clientSteamId}) on map {mapName}");
            return;
        }

        mid = new Vector(mid.X, mid.Y, mid.Z + 6.0f);
        const int lines = 20;
        const float minRadius = 20.0f;
        const float maxRadius = 100.0f;
        float pulse = (float)Math.Sin(_playerBeacons[player].PulsePhase * 2.0 * Math.PI);
        float radius = minRadius + (maxRadius - minRadius) * (pulse + 1.0f) / 2.0f;
        float step = (float)(2.0 * Math.PI) / lines;
        float angleOld = 0.0f;
        float angleCur = step;
        Color color = player.TeamNum == 2 ? Color.Red : Color.Blue;
        for (int i = 0; i < lines; i++)
        {
            var start = AngleOnCircle(angleOld, radius, mid);
            var end = AngleOnCircle(angleCur, radius, mid);
            var (index, beam) = DrawLaserBetween(start, end, color, 0.15f, 2.0f);
            if (beam != null)
            {
                Beams.Add(beam);
            }
            angleOld = angleCur;
            angleCur += step;
        }
        var (timer, _, _, _, _, pulsePhase) = _playerBeacons[player];
        _playerBeacons[player] = (timer, Beams, true, _playerBeacons[player].SoundTimer, _playerBeacons[player].RemainingSeconds, pulsePhase + 0.1f);
        if (_playerBeacons[player].PulsePhase >= 1.0f)
        {
            _playerBeacons[player] = (timer, Beams, true, _playerBeacons[player].SoundTimer, _playerBeacons[player].RemainingSeconds, 0.0f);
        }
    }

    private void PlayBeaconSound(CCSPlayerController player)
    {
        if (player.IsValid && _playerBeacons.ContainsKey(player) && _playerBeacons[player].Active)
        {
            if (Config.EnableSound == 1)
            {
                PlaySoundOnPlayer(player, Config.SoundPath);
            }
            var (timer, _, _, soundTimer, remainingSeconds, _) = _playerBeacons[player];
            _playerBeacons[player] = (timer, _playerBeacons[player].Beams, true, soundTimer, remainingSeconds - 1, _playerBeacons[player].PulsePhase);
            if (remainingSeconds <= 0)
            {
                StopBeacon(player);
            }
        }
    }

    private Vector AngleOnCircle(float angle, float radius, Vector mid)
    {
        return new Vector(
            (float)(mid.X + (radius * Math.Cos(angle))),
            (float)(mid.Y + (radius * Math.Sin(angle))),
            mid.Z
        );
    }

    private (int, CEnvBeam?) DrawLaserBetween(Vector startPos, Vector endPos, Color color, float life, float width)
    {
        if (startPos == null || endPos == null)
        {
            return (-1, null);
        }

        var beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam");
        if (beam == null)
        {
            Server.PrintToConsole("[BeaconPlugin] Failed to create env_beam entity");
            return (-1, null);
        }

        beam.Render = color;
        beam.Width = width;
        beam.Teleport(startPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));
        beam.EndPos.X = endPos.X;
        beam.EndPos.Y = endPos.Y;
        beam.EndPos.Z = endPos.Z;
        beam.DispatchSpawn();

        AddTimer(life, () =>
        {
            if (beam != null && beam.IsValid)
            {
                beam.Remove();
            }
        });

        return ((int)beam.Index, beam);
    }

    private void PlaySoundOnPlayer(CCSPlayerController player, string sound)
    {
        if (player.IsValid)
        {
            player.ExecuteClientCommand($"play {sound}");
        }
    }

    private void LogEvent(string message)
    {
        if (logFilePath != null)
        {
            try
            {
                File.AppendAllText(logFilePath, message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[BeaconPlugin] Error logging event: {ex.Message}");
            }
        }
    }
}