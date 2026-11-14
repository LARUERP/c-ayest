using System;
using System.Collections.Generic;
using System.Linq;

namespace FusionDarkRP.Telephone;

public sealed class PhoneCalls
{
    private sealed class Call
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string State = "Ringing";

        public ulong Caller;
        public ulong Callee;

        public bool CallerSpeaker;
        public bool CalleeSpeaker;

        public bool VideoRequested;
        public bool CallerVideo;
        public bool CalleeVideo;

        public bool Masked;
        public bool Answered;

        public double AnsweredAt;
        public DateTime Utc = DateTime.UtcNow;

        public string CallerToken = "";
        public string CalleeToken = "";

        public double Created;
        public double CallerSeen;
        public double CalleeSeen;
    }

    private readonly Dictionary<ulong, Call> byPlayer =
        new Dictionary<ulong, Call>();

    public Func<double> Clock { get; set; } =
        () => (double)Environment.TickCount64 / 1000.0;

    public event Action<PhoneCallResult> Finished;

    public bool Busy(ulong player)
    {
        return byPlayer.ContainsKey(player);
    }

    public ulong[] Start(
        ulong caller,
        ulong callee,
        double now,
        bool video = false,
        bool masked = false)
    {
        if (caller == callee)
            throw new InvalidOperationException(
                "Impossible de s'appeler soi-même.");

        if (Busy(caller))
            throw new InvalidOperationException("Tu es déjà en appel.");

        if (Busy(callee))
            throw new InvalidOperationException(
                "Ce correspondant est occupé.");

        Call call = new Call
        {
            Caller = caller,
            Callee = callee,
            Created = now,
            CallerSeen = now,
            CalleeSeen = now,
            VideoRequested = video,
            CallerVideo = video,
            Masked = masked
        };

        byPlayer.Add(caller, call);
        byPlayer.Add(callee, call);

        return new ulong[] { caller, callee };
    }

    private Call Get(ulong actor, string token)
    {
        if (!byPlayer.TryGetValue(actor, out var call) ||
            string.IsNullOrEmpty(token) ||
            call.Id != token)
        {
            throw new InvalidOperationException(
                "Cet appel est terminé.");
        }

        return call;
    }

    public ulong[] Answer(
        ulong actor,
        string token,
        double now,
        bool video = false)
    {
        Call call = Get(actor, token);

        if (actor != call.Callee || call.State != "Ringing")
            throw new InvalidOperationException(
                "Aucun appel entrant à accepter.");

        if (now - call.Created >= 30.0)
            throw new InvalidOperationException("Appel expiré.");

        call.Answered = true;
        call.AnsweredAt = now;
        call.CalleeVideo = video;
        call.State = "Active";
        call.CallerSeen = call.CalleeSeen = now;

        return new ulong[] { call.Caller, call.Callee };
    }

    public ulong[] Reject(ulong actor, string token)
    {
        Call call = Get(actor, token);

        if (actor != call.Callee || call.State != "Ringing")
            throw new InvalidOperationException(
                "Aucun appel entrant à refuser.");

        return End(call, "declined");
    }

    public ulong[] Hangup(ulong actor, string token)
    {
        Call call = Get(actor, token);

        return End(
            call,
            call.State == "Active"
                ? "completed"
                : actor == call.Caller
                    ? "cancelled"
                    : "declined");
    }

    public void Heartbeat(
        ulong actor,
        string token,
        double now)
    {
        Call call = Get(actor, token);

        if (actor == call.Caller)
            call.CallerSeen = now;
        else
            call.CalleeSeen = now;
    }

    public bool TryRoute(
        ulong actor,
        string token,
        double now,
        out ulong recipient)
    {
        recipient = 0UL;

        if (!byPlayer.TryGetValue(actor, out var call) ||
            call.Id != token ||
            call.State != "Active" ||
            now - call.CallerSeen >= 12.0 ||
            now - call.CalleeSeen >= 12.0)
        {
            return false;
        }

        recipient = actor == call.Caller
            ? call.Callee
            : call.Caller;

        return true;
    }

    public PhoneCallView View(
        ulong actor,
        Func<ulong, string> number)
    {
        if (!byPlayer.TryGetValue(actor, out var call))
            return new PhoneCallView();

        ulong peer = actor == call.Caller
            ? call.Callee
            : call.Caller;

        return new PhoneCallView
        {
            VideoRequested = call.VideoRequested,
            Video = actor == call.Caller
                ? call.CallerVideo
                : call.CalleeVideo,
            PeerVideo = actor == call.Caller
                ? call.CalleeVideo
                : call.CallerVideo,
            Id = call.Id,
            State = call.State,
            Peer = peer,
            Masked = call.Masked,
            Number = call.Masked && actor == call.Callee
                ? ""
                : number(peer),
            Speaker = actor == call.Caller
                ? call.CallerSpeaker
                : call.CalleeSpeaker,
            PeerSpeaker = actor == call.Caller
                ? call.CalleeSpeaker
                : call.CallerSpeaker,
            Incoming = actor == call.Callee
        };
    }

    public ulong[] SetVideo(
        ulong actor,
        string token,
        bool enabled)
    {
        Call call = Get(actor, token);

        if (call.State != "Active")
            throw new InvalidOperationException(
                "Accepter l'appel avant la caméra.");

        if (actor == call.Caller)
            call.CallerVideo = enabled;
        else
            call.CalleeVideo = enabled;

        return new ulong[] { call.Caller, call.Callee };
    }

    public bool VideoEnabled(ulong actor, string token)
    {
        return byPlayer.TryGetValue(actor, out var call) &&
            call.Id == token &&
            call.State == "Active" &&
            (actor == call.Caller
                ? call.CallerVideo
                : call.CalleeVideo);
    }

    public ulong[] SetSpeaker(
        ulong actor,
        string token,
        bool enabled)
    {
        Call call = Get(actor, token);

        if (call.State != "Active")
            throw new InvalidOperationException(
                "Accepter l'appel avant d'activer le haut-parleur.");

        if (actor == call.Caller &&
            call.CallerSpeaker != enabled)
        {
            call.CallerSpeaker = enabled;
            call.CallerToken = Guid.NewGuid().ToString("N");
        }

        if (actor == call.Callee &&
            call.CalleeSpeaker != enabled)
        {
            call.CalleeSpeaker = enabled;
            call.CalleeToken = Guid.NewGuid().ToString("N");
        }

        return new ulong[] { call.Caller, call.Callee };
    }

    public (ulong Owner, string Token)[] Speakers(
        ulong actor,
        string token)
    {
        if (!byPlayer.TryGetValue(actor, out var call) ||
            call.Id != token ||
            call.State != "Active")
        {
            return Array.Empty<(ulong, string)>();
        }

        List<(ulong, string)> speakers =
            new List<(ulong, string)>();

        if (call.CallerSpeaker)
            speakers.Add((call.Caller, call.CallerToken));

        if (call.CalleeSpeaker)
            speakers.Add((call.Callee, call.CalleeToken));

        return speakers.ToArray();
    }

    private ulong[] End(Call call, string outcome = null)
    {
        byPlayer.Remove(call.Caller);
        byPlayer.Remove(call.Callee);

        Finished?.Invoke(new PhoneCallResult
        {
            Id = call.Id,
            Caller = call.Caller,
            Callee = call.Callee,
            Masked = call.Masked,
            Answered = call.Answered,
            Utc = call.Utc,
            Outcome = outcome ??
                (call.Answered ? "completed" : "missed"),
            Seconds = call.Answered
                ? (int)Math.Min(
                    86400.0,
                    Math.Max(0.0, Clock() - call.AnsweredAt))
                : 0
        });

        return new ulong[] { call.Caller, call.Callee };
    }

    public ulong[] Disconnect(ulong actor)
    {
        return byPlayer.TryGetValue(actor, out var call)
            ? End(call)
            : Array.Empty<ulong>();
    }

    public ulong[] Tick(double now)
    {
        Call[] expired = byPlayer.Values
            .Distinct()
            .Where(call =>
                call.State == "Ringing"
                    ? now - call.Created >= 30.0
                    : now - call.CallerSeen >= 12.0 ||
                      now - call.CalleeSeen >= 12.0)
            .ToArray();

        return expired
            .SelectMany(call => End(call))
            .Distinct()
            .ToArray();
    }

    public ulong[] StopAll()
    {
        Call[] calls = byPlayer.Values.Distinct().ToArray();

        return calls
            .SelectMany(call => End(call))
            .Distinct()
            .ToArray();
    }
}
