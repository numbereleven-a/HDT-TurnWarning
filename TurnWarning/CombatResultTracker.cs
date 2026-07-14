using System;

namespace TurnWarning
{
	internal enum CombatSide
	{
		Player,
		Opponent
	}

	internal enum CombatOutcome
	{
		Win,
		Loss,
		Tie
	}

	internal sealed class CombatResultSummary
	{
		public CombatResultSummary(CombatOutcome outcome, int damage)
		{
			Outcome = outcome;
			Damage = damage;
		}

		public CombatOutcome Outcome { get; }
		public int Damage { get; }
		public string OutcomeLabel => Outcome switch
		{
			CombatOutcome.Win => "Win",
			CombatOutcome.Loss => "Loss",
			_ => "Tie"
		};
		public string Detail => Outcome switch
		{
			CombatOutcome.Win => $"{Damage} damage dealt",
			CombatOutcome.Loss => $"{Damage} damage taken",
			_ => "No hero damage"
		};
		public string Text => $"Previous combat: {OutcomeLabel} - {Detail.ToLowerInvariant()}";
	}

	internal sealed class CombatResultTracker
	{
		private readonly object _gate = new object();
		private bool _active;
		private bool _opponentWasAlive;
		private int _damageTaken;
		private int _damageDealt;

		public void Begin(bool opponentWasAlive)
		{
			lock(_gate)
			{
				_active = true;
				_opponentWasAlive = opponentWasAlive;
				_damageTaken = 0;
				_damageDealt = 0;
			}
		}

		public void RecordHeroDamage(CombatSide side, int damage)
		{
			lock(_gate)
			{
				if(!_active || damage <= 0)
					return;
				if(side == CombatSide.Player)
					_damageTaken = Math.Max(_damageTaken, damage);
				else
					_damageDealt = Math.Max(_damageDealt, damage);
			}
		}

		public CombatResultSummary? Complete()
		{
			lock(_gate)
			{
				if(!_active)
					return null;
				_active = false;
				if(_damageTaken > 0 && _damageDealt == 0)
					return new CombatResultSummary(CombatOutcome.Loss, _damageTaken);
				if(_damageDealt > 0 && _damageTaken == 0)
					return new CombatResultSummary(CombatOutcome.Win, _damageDealt);
				if(_damageTaken == 0 && _damageDealt == 0 && _opponentWasAlive)
					return new CombatResultSummary(CombatOutcome.Tie, 0);
				return null;
			}
		}

		public void Reset()
		{
			lock(_gate)
			{
				_active = false;
				_opponentWasAlive = false;
				_damageTaken = 0;
				_damageDealt = 0;
			}
		}
	}
}
