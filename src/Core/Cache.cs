using System;

namespace AnotherECS.Remote.Fusion
{
    public struct Cache<T>
    {
        private readonly Func<T> _update;
        private bool _isDirty;
        private T _data;
        public Cache(Func<T> update)
        {
            _isDirty = true;
            _update = update;
            _data = default;
        }

        public T Get()
        {
            if (_isDirty)
            {
                _isDirty = false;
                _data = _update();
            }
            return _data;
        }

        public void Drop()
        {
            _isDirty = true;
        }
    }
}
