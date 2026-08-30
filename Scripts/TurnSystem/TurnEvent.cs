using System;

namespace MbcPrototype.TurnSystem;

/// <summary>
/// A discrete unit of work that must finish before the current turn's resolution
/// completes and control returns to the player.
///
/// The GameManager keeps a registry of pending TurnEvents: the turn ends only
/// when every registered event has been marked resolved. This is the extension
/// point for future event types (chain reactions, environmental effects,
/// multi-stage attacks, long animations, etc.) — any script can create a
/// TurnEvent, register it with GameManager, and call MarkResolved() when its
/// work is done, and the turn will not end until then.
/// </summary>
public class TurnEvent
{
	/// <summary>Human-readable description, useful for debug logging.</summary>
	public string Description { get; }

	/// <summary>True once this event has finished its work.</summary>
	public bool IsResolved { get; private set; }

	/// <summary>Fired exactly once, the first time MarkResolved() is called.</summary>
	public event Action Resolved;

	public TurnEvent(string description = "TurnEvent")
	{
		Description = description;
	}

	/// <summary>
	/// Marks this event as finished. Safe to call multiple times — the
	/// Resolved signal only fires on the first call.
	/// </summary>
	public void MarkResolved()
	{
		if (IsResolved) return;
		IsResolved = true;
		Resolved?.Invoke();
	}
}
