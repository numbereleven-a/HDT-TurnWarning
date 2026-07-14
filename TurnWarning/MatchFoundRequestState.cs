using System;

namespace TurnWarning
{
	internal sealed class MatchFoundRequestState
	{
		private readonly object _gate = new object();
		private int _generation;
		private bool _pending;
		private DateTime _deadlineUtc;

		public int CurrentGeneration
		{
			get
			{
				lock(_gate)
					return _generation;
			}
		}

		public void Start(bool pending, DateTime deadlineUtc)
		{
			lock(_gate)
			{
				_generation++;
				_pending = pending;
				_deadlineUtc = deadlineUtc;
			}
		}

		public void End()
		{
			lock(_gate)
			{
				_generation++;
				_pending = false;
				_deadlineUtc = default;
			}
		}

		public void CancelPending()
		{
			lock(_gate)
				_pending = false;
		}

		public bool TryGetPending(out int generation, out DateTime deadlineUtc)
		{
			lock(_gate)
			{
				generation = _generation;
				deadlineUtc = _deadlineUtc;
				return _pending;
			}
		}

		public bool CancelIfCurrent(int generation)
		{
			lock(_gate)
			{
				if(generation != _generation || !_pending)
					return false;
				_pending = false;
				return true;
			}
		}

		public bool TryComplete(int generation)
			=> CancelIfCurrent(generation);

		public bool IsCurrent(int generation)
		{
			lock(_gate)
				return generation == _generation;
		}
	}
}
