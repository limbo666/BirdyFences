using System;
using System.Collections.Generic;
using System.Timers;

namespace Birdy_Fences
{
    public class TargetChecker
    {
        private readonly Timer _timer;
        private readonly Dictionary<string, (Action checkAction, bool isFolder)> _checkActions;

        public TargetChecker(double interval)
        {
            _timer = new Timer(interval);
            _timer.Elapsed += OnTimedEvent;
            _checkActions = new Dictionary<string, (Action, bool)>();
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void AddCheckAction(string key, Action checkAction, bool isFolder)
        {
            if (!_checkActions.ContainsKey(key))
            {
                _checkActions.Add(key, (checkAction, isFolder));
            }
        }

        public void RemoveCheckAction(string key)
        {
            if (_checkActions.ContainsKey(key))
            {
                _checkActions.Remove(key);
            }
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            foreach (var (checkAction, isFolder) in _checkActions.Values)
            {
                checkAction.Invoke();
            }
        }
    }
}
