using Verse.ParserDescriptors.Recurse;

namespace Verse.ParserDescriptors.Recurse.Readers.String.Nodes
{
    class EmptyNode<TEntity, TValue, TState> : INode<TEntity, TValue, TState>
    {
        public bool IsConnected
        {
            get
            {
                return false;
            }
        }

        #region Attributes

        public static readonly EmptyNode<TEntity, TValue, TState> Instance = new EmptyNode<TEntity, TValue, TState>();

        #endregion

        #region Methods

        public void Assign(ref TEntity target, TValue value)
        {
        }

        public bool Enter(ref TEntity target, IReader<TEntity, TValue, TState> unknown, TState state)
        {
            return unknown.ReadValue(ref target, state);
        }

        public INode<TEntity, TValue, TState> Follow(char c)
        {
            return this;
        }

        #endregion
    }
}