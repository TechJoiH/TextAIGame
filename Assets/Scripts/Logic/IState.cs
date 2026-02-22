public interface IState<T>
{
    void Enter(T owner);
    void Exit(T owner);
    void Update(T owner);
}