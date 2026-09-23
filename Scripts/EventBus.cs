using System;
using System.Collections.Generic;
using Godot;
using Lizzie.AssetManagement;

/// <summary>
/// Global event bus for decoupled communication between components
/// Add this as an AutoLoad singleton in Godot project settings
/// </summary>
public partial class EventBus : Node
{
    private static EventBus _instance;

    public static EventBus Instance
    {
        get
        {
            if (_instance == null)
            {
                GD.PrintErr(
                    "EventBus instance not initialized. Make sure EventBus is added as an AutoLoad."
                );
            }
            return _instance;
        }
    }

    // Dictionary to store event subscriptions by event type
    private readonly Dictionary<Type, Delegate> _eventDelegates = new();

    public override void _EnterTree()
    {
        _instance = this;
    }

    #region Subscribe/Unsubscribe

    /// <summary>
    /// Subscribe to an event without parameters
    /// </summary>
    public void Subscribe<TEvent>(Action callback)
        where TEvent : IEvent
    {
        var eventType = typeof(TEvent);

        if (_eventDelegates.ContainsKey(eventType))
        {
            _eventDelegates[eventType] = Delegate.Combine(_eventDelegates[eventType], callback);
        }
        else
        {
            _eventDelegates[eventType] = callback;
        }
    }

    /// <summary>
    /// Subscribe to an event with parameters
    /// </summary>
    public void Subscribe<TEvent>(Action<TEvent> callback)
        where TEvent : IEvent
    {
        var eventType = typeof(TEvent);

        if (_eventDelegates.ContainsKey(eventType))
        {
            _eventDelegates[eventType] = Delegate.Combine(_eventDelegates[eventType], callback);
        }
        else
        {
            _eventDelegates[eventType] = callback;
        }
    }

    /// <summary>
    /// Unsubscribe from an event without parameters
    /// </summary>
    public void Unsubscribe<TEvent>(Action callback)
        where TEvent : IEvent
    {
        var eventType = typeof(TEvent);

        if (_eventDelegates.ContainsKey(eventType))
        {
            _eventDelegates[eventType] = Delegate.Remove(_eventDelegates[eventType], callback);

            if (_eventDelegates[eventType] == null)
            {
                _eventDelegates.Remove(eventType);
            }
        }
    }

    /// <summary>
    /// Unsubscribe from an event with parameters
    /// </summary>
    public void Unsubscribe<TEvent>(Action<TEvent> callback)
        where TEvent : IEvent
    {
        var eventType = typeof(TEvent);

        if (_eventDelegates.ContainsKey(eventType))
        {
            _eventDelegates[eventType] = Delegate.Remove(_eventDelegates[eventType], callback);

            if (_eventDelegates[eventType] == null)
            {
                _eventDelegates.Remove(eventType);
            }
        }
    }

    #endregion

    #region Publish

    /// <summary>
    /// Returns false if the delegate target is a freed Godot object.
    /// Non-Godot targets (plain C# classes) are always considered valid.
    /// </summary>
    private static bool IsTargetValid(Delegate d)
    {
        return d.Target is not GodotObject obj || IsInstanceValid(obj);
    }

    /// <summary>
    /// Publish an event without parameters
    /// </summary>
    public void Publish<TEvent>()
        where TEvent : IEvent, new()
    {
        var eventType = typeof(TEvent);

        if (!_eventDelegates.TryGetValue(eventType, out var eventDelegate))
            return;

        var instance = new TEvent();
        Delegate survivors = null;

        foreach (var d in eventDelegate.GetInvocationList())
        {
            if (!IsTargetValid(d))
                continue;

            survivors = Delegate.Combine(survivors, d);

            switch (d)
            {
                case Action action:
                    action();
                    break;
                case Action<TEvent> typed:
                    typed(instance);
                    break;
            }
        }

        if (survivors == null)
            _eventDelegates.Remove(eventType);
        else
            _eventDelegates[eventType] = survivors;
    }

    /// <summary>
    /// Publish an event with parameters
    /// </summary>
    public void Publish<TEvent>(TEvent eventData)
        where TEvent : IEvent
    {
        var eventType = typeof(TEvent);

        if (!_eventDelegates.TryGetValue(eventType, out var eventDelegate))
            return;

        Delegate survivors = null;

        foreach (var d in eventDelegate.GetInvocationList())
        {
            if (!IsTargetValid(d))
                continue;

            survivors = Delegate.Combine(survivors, d);

            if (d is Action<TEvent> typed)
                typed(eventData);
        }

        if (survivors == null)
            _eventDelegates.Remove(eventType);
        else
            _eventDelegates[eventType] = survivors;
    }

    #endregion

    #region Utility

    /// <summary>
    /// Clear all subscriptions for a specific event type
    /// </summary>
    public void ClearSubscriptions<TEvent>()
        where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        _eventDelegates.Remove(eventType);
    }

    /// <summary>
    /// Clear all event subscriptions
    /// </summary>
    public void ClearAllSubscriptions()
    {
        _eventDelegates.Clear();
        GD.Print("All EventBus subscriptions cleared");
    }

    /// <summary>
    /// Check if an event type has any subscribers
    /// </summary>
    public bool HasSubscribers<TEvent>()
        where TEvent : IEvent
    {
        return _eventDelegates.ContainsKey(typeof(TEvent));
    }

    #endregion
}

/// <summary>
/// Base interface for all events
/// Implement this interface on your event classes
/// </summary>
public interface IEvent { }

#region Event Definitions

public class ShowTemplateEditor : IEvent
{
    public SnowTag TemplateRef { get; set; }
}

public class ShowDatasetEditor : IEvent
{
    public SnowTag DatasetRef { get; set; }
}

public class ShowImageManagerEvent : IEvent
{
    public Guid ImageReference { get; set; }
}

public class EditPrototypeEvent : IEvent
{
    public SnowTag PrototypeId { get; set; }
}

public class MakePrototypeUniqueEvent : IEvent
{
    public SnowTag PrototypeId { get; set; }
}

/// <summary>
/// When a dialog is opened, this event is published to disable inputs
/// </summary>
public class ModalDialogOpenedEvent : IEvent { }

/// <summary>
/// When a dialog is closed, this event is published to re-enable inputs
/// </summary>
public class ModalDialogClosedEvent : IEvent { }

public class SpawnPrototypeEvent : IEvent
{
    public SnowTag PrototypeRef { get; set; }
    public int DataSetRowIndex { get; set; } = -1;
    public SnowTag DataSetRowId { get; set; } = SnowTag.Empty;
}

public class QueueStackingUpdateEvent : IEvent { }

public class ShowComponentPreviewDialogEvent(VisualComponentBase component) : IEvent
{
    public VisualComponentBase Component { get; set; } = component;
}

/// <summary>
/// Published on a client when it has connected to an existing game.
/// </summary>
public class LocalPlayerJoinedGameEvent : IEvent { }

/// <summary>
/// Published when the local player should be prompted to pick a player position.
/// </summary>
public class RequestPlayerPositionEvent : IEvent { }

#endregion
