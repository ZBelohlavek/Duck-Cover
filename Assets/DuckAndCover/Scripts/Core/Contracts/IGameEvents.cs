using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// One stream of rule events that Net, Game and UI subscribe to. Rules publish; nothing outside
    /// Core publishes rule events.
    /// </summary>
    public interface IGameEvents
    {
        void Publish(GameEvent e);
        void Subscribe(Action<GameEvent> handler);
        void Unsubscribe(Action<GameEvent> handler);
    }

    /// <summary>
    /// Plain synchronous implementation of <see cref="IGameEvents"/>. Handlers run in subscription
    /// order. A handler that subscribes or unsubscribes during a publish takes effect next publish.
    /// Keeps a history when <see cref="Record"/> is on, which tests use to assert what happened.
    /// </summary>
    public class GameEventBus : IGameEvents
    {
        readonly List<Action<GameEvent>> handlers = new List<Action<GameEvent>>();
        public readonly List<GameEvent> History = new List<GameEvent>();
        public bool Record;

        public void Publish(GameEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (Record) History.Add(e);
            foreach (var h in handlers.ToArray()) h(e);
        }

        public void Subscribe(Action<GameEvent> handler)
        {
            if (handler != null && !handlers.Contains(handler)) handlers.Add(handler);
        }

        public void Unsubscribe(Action<GameEvent> handler) => handlers.Remove(handler);
    }
}
