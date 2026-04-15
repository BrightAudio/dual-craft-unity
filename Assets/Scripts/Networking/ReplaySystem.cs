// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Replay System (Move History & Playback)
//
//  "Store move histories for auditing disputes. Use
//   replay verification to detect inconsistencies."
//
//  Records every action during a game and serializes
//  a complete ReplayData on game end. Can be used for:
//   - Dispute resolution
//   - Spectator playback
//   - Anti-cheat verification
//   - Post-game analysis
//  Pure C# — no Unity dependencies.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace DualCraft.Networking
{
    /// <summary>
    /// Records moves during a live game. Call <see cref="Begin"/>
    /// at game start, <see cref="RecordMove"/> after each action,
    /// and <see cref="Finish"/> at game end to get the complete replay.
    /// </summary>
    public class ReplayRecorder
    {
        private readonly string _roomId;
        private ReplayData _data;
        private bool _recording;

        public ReplayRecorder(string roomId)
        {
            _roomId = roomId;
        }

        /// <summary>Start recording a new game.</summary>
        public void Begin(string player0, string player1,
                          string deck0, string deck1, int shuffleSeed)
        {
            _data = new ReplayData
            {
                RoomId = _roomId,
                GameMode = "standard",
                StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PlayerNames = new[] { player0, player1 },
                DeckIds = new[] { deck0, deck1 },
                ShuffleSeed = shuffleSeed,
                Moves = new List<ReplayEntry>(),
            };
            _recording = true;
        }

        /// <summary>Record a single move.</summary>
        public void RecordMove(int turn, int playerIndex,
                               SerializableAction action,
                               bool success, string reason)
        {
            if (!_recording) return;

            _data.Moves.Add(new ReplayEntry
            {
                TurnNumber = turn,
                PlayerIndex = playerIndex,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Action = action,
                Success = success,
                Reason = reason ?? "",
            });
        }

        /// <summary>
        /// Finish recording and return the sealed replay data.
        /// </summary>
        public ReplayData Finish(int winnerIndex, string winReason)
        {
            if (!_recording) return null;
            _recording = false;

            _data.EndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _data.WinnerIndex = winnerIndex;
            _data.WinReason = winReason;

            return _data;
        }

        /// <summary>Whether a game is currently being recorded.</summary>
        public bool IsRecording => _recording;

        /// <summary>Number of moves recorded so far.</summary>
        public int MoveCount => _data?.Moves?.Count ?? 0;
    }

    /// <summary>
    /// Replays a recorded game action-by-action.
    /// Feed it a ReplayData and step through moves.
    /// </summary>
    public class ReplayPlayer
    {
        private readonly ReplayData _replay;
        private int _cursor;

        public ReplayPlayer(ReplayData replay)
        {
            _replay = replay ?? throw new ArgumentNullException(nameof(replay));
            _cursor = 0;
        }

        /// <summary>Total number of moves.</summary>
        public int TotalMoves => _replay.Moves.Count;

        /// <summary>Current position in the replay.</summary>
        public int CurrentMove => _cursor;

        /// <summary>True if there are more moves to play.</summary>
        public bool HasNext => _cursor < _replay.Moves.Count;

        /// <summary>Room and player info.</summary>
        public ReplayData Info => _replay;

        /// <summary>Get the next move without advancing.</summary>
        public ReplayEntry Peek()
        {
            return HasNext ? _replay.Moves[_cursor] : null;
        }

        /// <summary>Advance and return the next move.</summary>
        public ReplayEntry Next()
        {
            return HasNext ? _replay.Moves[_cursor++] : null;
        }

        /// <summary>Go back one step.</summary>
        public ReplayEntry Previous()
        {
            if (_cursor <= 0) return null;
            _cursor--;
            return _replay.Moves[_cursor];
        }

        /// <summary>Jump to a specific move index.</summary>
        public void SeekTo(int moveIndex)
        {
            _cursor = Math.Clamp(moveIndex, 0, _replay.Moves.Count);
        }

        /// <summary>Reset to the beginning.</summary>
        public void Reset() => _cursor = 0;
    }
}
