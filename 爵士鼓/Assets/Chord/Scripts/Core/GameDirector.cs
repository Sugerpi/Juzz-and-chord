using System;
using UnityEngine;
using ChordPlayer.Kinect;
using ChordPlayer.Music;

namespace ChordPlayer.Core
{
    /// <summary>
    /// Top-level state machine. Idle (no body) → Tracking (body seen) →
    /// Playing (a chord is sounding). Other systems read <see cref="State"/>
    /// (HUD, indicators, Phase 6 fade-out) instead of re-deriving it.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        public enum AppState { Idle, Tracking, Playing }

        public AppState State { get; private set; } = AppState.Idle;
        public event Action<AppState> StateChanged;

        IBodyInputProvider _input;
        ChordEngine _engine;

        void OnEnable() => ServiceLocator.Register(this);
        void OnDisable() => ServiceLocator.Unregister<GameDirector>();

        void Update()
        {
            if (_input == null) ServiceLocator.TryGet(out _input);
            if (_engine == null) ServiceLocator.TryGet(out _engine);

            AppState next = AppState.Idle;
            if (_input != null && _input.Body.IsTracked) next = AppState.Tracking;
            if (_engine != null && _engine.IsPlaying) next = AppState.Playing;

            if (next != State)
            {
                State = next;
                StateChanged?.Invoke(State);
            }
        }
    }
}
