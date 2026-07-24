namespace TurnWarning
{
	internal enum TurnPhase
	{
		OutOfGame,
		Unknown,
		Recruit,
		Combat,
		Ended
	}

	internal enum PhaseTransition
	{
		None,
		EnteredCombat,
		EnteredRecruit
	}

	internal sealed class TurnToken
	{
		public TurnToken(int session, int turn)
		{
			Session = session;
			Turn = turn;
		}

		public int Session { get; }
		public int Turn { get; }
	}

	internal sealed class CombatToken
	{
		public CombatToken(int session, int sequence)
		{
			Session = session;
			Sequence = sequence;
		}

		public int Session { get; }
		public int Sequence { get; }
	}

	internal sealed class TurnBoundaryTracker
	{
		private readonly object _gate = new object();
		private int _session;
		private int _lastProcessedTurn = -1;
		private bool _combatArmed;
		private bool _recruitBoundaryArmed;
		private bool _combatNotificationArmed;
		private int _combatNotificationSequence;
		private bool _lastObservedCombat;
		private TurnPhase _phase = TurnPhase.OutOfGame;

		public TurnPhase Phase
		{
			get
			{
				lock(_gate)
					return _phase;
			}
		}

		public void Initialize(bool matchActive, bool isBattlegrounds, bool isCombat, bool isReconnect)
		{
			lock(_gate)
			{
				StartNewSession();
				_lastObservedCombat = isCombat;
				if(!matchActive || !isBattlegrounds)
				{
					_phase = TurnPhase.OutOfGame;
					return;
				}

				_phase = isCombat ? TurnPhase.Combat : TurnPhase.Recruit;
				_combatNotificationArmed = !isCombat;
				// A reconnect can replay the current phase. Skip that boundary and arm only
				// after a fresh Recruit -> Combat transition has been observed.
				_combatArmed = isCombat && !isReconnect;
			}
		}

		public void OnGameStarted(bool isBattlegrounds)
		{
			lock(_gate)
			{
				StartNewSession();
				_phase = isBattlegrounds ? TurnPhase.Recruit : TurnPhase.Unknown;
				_combatNotificationArmed = isBattlegrounds;
			}
		}

		public void OnGameEnded()
		{
			lock(_gate)
			{
				StartNewSession();
				_phase = TurnPhase.Ended;
			}
		}

		public PhaseTransition Observe(bool matchActive, bool isBattlegrounds, bool isCombat)
		{
			lock(_gate)
			{
				if(!matchActive || !isBattlegrounds)
				{
					if(_phase != TurnPhase.OutOfGame)
					{
						StartNewSession();
						_phase = TurnPhase.OutOfGame;
					}
					return PhaseTransition.None;
				}

				if(_phase is TurnPhase.OutOfGame or TurnPhase.Ended)
				{
					// We attached after game start. Establish a baseline without producing
					// a historical warning for the phase already in progress.
					StartNewSession();
					_phase = isCombat ? TurnPhase.Combat : TurnPhase.Recruit;
					_combatArmed = false;
					_combatNotificationArmed = !isCombat;
					_lastObservedCombat = isCombat;
					return PhaseTransition.None;
				}
				if(_phase == TurnPhase.Unknown)
				{
					_phase = isCombat ? TurnPhase.Combat : TurnPhase.Recruit;
					_combatNotificationArmed = !isCombat;
					_lastObservedCombat = isCombat;
					return PhaseTransition.None;
				}

				var enteredCombat = isCombat && !_lastObservedCombat;
				var leftCombat = !isCombat && _lastObservedCombat;
				_lastObservedCombat = isCombat;

				if(enteredCombat)
				{
					_phase = TurnPhase.Combat;
					_combatArmed = true;
					_recruitBoundaryArmed = false;
					return PhaseTransition.EnteredCombat;
				}
				else if(leftCombat)
				{
					_phase = TurnPhase.Recruit;
					_recruitBoundaryArmed = _combatArmed;
					_combatArmed = false;
					return PhaseTransition.EnteredRecruit;
				}
				return PhaseTransition.None;
			}
		}

		public CombatToken? OnCombatStarted()
		{
			lock(_gate)
			{
				if(_phase != TurnPhase.Combat || !_lastObservedCombat || !_combatNotificationArmed)
					return null;
				_combatNotificationArmed = false;
				_combatNotificationSequence++;
				return new CombatToken(_session, _combatNotificationSequence);
			}
		}

		public TurnToken? OnPlayerTurnStarted(int turn, bool matchActive, bool isBattlegrounds, bool isCombat)
		{
			lock(_gate)
			{
				if(!matchActive || !isBattlegrounds)
					return null;

				var wasArmed = _phase == TurnPhase.Combat
					? _combatArmed
					: _phase == TurnPhase.Recruit && _recruitBoundaryArmed;
				_combatArmed = false;
				_recruitBoundaryArmed = false;
				_phase = TurnPhase.Recruit;

				if(!wasArmed || turn <= 1 || turn == _lastProcessedTurn)
					return null;

				_lastProcessedTurn = turn;
				_combatNotificationArmed = true;
				return new TurnToken(_session, turn);
			}
		}

		public bool IsCurrentCombat(CombatToken token, bool matchActive, bool isBattlegrounds, bool isCombat)
		{
			lock(_gate)
			{
				return token.Session == _session
					&& token.Sequence == _combatNotificationSequence
					&& matchActive
					&& isBattlegrounds
					&& isCombat
					&& _lastObservedCombat
					&& _phase == TurnPhase.Combat;
			}
		}

		public bool IsCurrent(TurnToken token, int turn, bool matchActive, bool isBattlegrounds, bool isCombat)
		{
			lock(_gate)
			{
				return token.Session == _session
					&& token.Turn == turn
					&& token.Turn == _lastProcessedTurn
					&& matchActive
					&& isBattlegrounds
					&& !isCombat
					&& _phase == TurnPhase.Recruit;
			}
		}

		private void StartNewSession()
		{
			_session++;
			_lastProcessedTurn = -1;
			_combatArmed = false;
			_recruitBoundaryArmed = false;
			_combatNotificationArmed = false;
			_combatNotificationSequence = 0;
			_lastObservedCombat = false;
		}
	}
}
