namespace MozartWorkflows.Services.Interfaces
{
    public interface IDbService
    {
        T? GetById<T>(int id, string sqlQuery);
        List<T> GetAll<T>(string sqlQuery);
        List<int> Query(string query);
        List<T> GetAllWithParams<T>(string query, IEnumerable<int> Id);
        T ExecuteProcSingle<T>(string procName, object param);
        void ExecuteProcedureVoid(string procName, object param);
        void ExecuteProcedurevoid(string procName, object param);
        int Add<T>(T entity, string sqlQuery);
        int Update<T>(T entity, string sqlQuery);
        int Delete(int id, string sqlQuery);
        List<T> ExecuteProcedure<T>(string procName, object param);
        Task<IEnumerable<T>> ExecuteProcedureAsync<T>(string procName, object param);
        Task ExecuteProcedureAsync(string procName, object param);

        T QuerySingle<T>(string sqlQuery);
        int ExecuteProc(string procName, object param);
        List<T> QueryList<T>(string query);

        Task<T?> GetAsync<T>(string query, object? parameters = null);
        Task<IEnumerable<T>> GetAllAsync<T>(string query, object? parameters = null);
        Task<int> EditData(string command, object parms);
        public List<T> ExecuteProcZeroTimeout<T>(string procName, object param);

        Task InsertJsonDataAsync(string tableName, string jsonData);
        Task<List<List<object>>> ExecuteMultipleTablesAsync(string procName, object param);
        void Execute(string query, object? parameters = null);
        T? QueryFirstOrDefault<T>(string query, object? parameters = null);
        Task ExecuteAsync(string query, object? parameters = null);
        Task<T?> QueryFirstOrDefaultAsync<T>(string query, object? parameters = null);
        Task<IEnumerable<T>> QueryAsync<T>(string query, object? parameters = null);


    }
}
