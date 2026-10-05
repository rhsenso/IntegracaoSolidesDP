using Dapper;
using IntegracaoSolidesDP.Worker.Source;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IntegracaoSolidesDP.Tests.Sql;

/// <summary>
/// SQL Server real (Testcontainers) com as tabelas do RHSenso usadas pelo worker. Os tipos
/// das colunas são os do bd_rhu_adn (char com padding, datetime, varchar(11) do CPF etc.).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string Database = "bd_rhu_adn";

    private const string RhuSchema = """
        CREATE TABLE dbo.tsitu1 (cdsituacao char(2) NOT NULL PRIMARY KEY, dcsituacao varchar(40) NULL, fldemissao char(1) NULL);
        CREATE TABLE dbo.tcus1 (cdccusto char(5) NOT NULL PRIMARY KEY, dcccusto varchar(100) NOT NULL);
        CREATE TABLE dbo.cargo1 (cdcargo char(5) NOT NULL PRIMARY KEY, dccargo varchar(40) NOT NULL, cdcbo6 char(6) NULL);
        CREATE TABLE dbo.test1 (cdempresa int NOT NULL, cdfilial int NOT NULL, nmfantasia varchar(30) NULL, dcestab varchar(60) NULL, cdcgc char(15) NULL,
                                PRIMARY KEY (cdempresa, cdfilial));
        CREATE TABLE dbo.func1 (
            id uniqueidentifier NOT NULL PRIMARY KEY, nomatric char(8) NOT NULL, nmcolab char(60) NULL, cdempresa int NOT NULL, cdfilial int NOT NULL,
            cdccusto char(5) NULL, tpcolab int NULL, dtdemissao datetime NULL, dtadmissao datetime NOT NULL, dttransf datetime NULL,
            cdcausres char(2) NULL, nocpf varchar(11) NULL, nopis varchar(11) NULL, nocartprof varchar(10) NULL, noserie char(5) NULL,
            dtnasc datetime NULL, cdsexo char(1) NULL, cdestcivil char(1) NULL, cdinstruc char(2) NULL, cod_raca int NULL,
            dcemail varchar(80) NULL, emailalternativo varchar(60) NULL, noddd varchar(3) NULL, notelefone varchar(30) NULL,
            nmmaecolab varchar(60) NULL, nmpaicolab varchar(60) NULL, cdcargo char(5) NULL, cdsituacao char(2) NULL);
        CREATE TABLE dbo.feria2 (
            id uniqueidentifier NOT NULL PRIMARY KEY, nomatric char(8) NOT NULL, cdempresa int NOT NULL, cdfilial int NOT NULL,
            dtinipa datetime NULL, dtinipf datetime NOT NULL, dtfimpf datetime NULL, qtdiasfe int NULL, qtabono int NULL, flconfirm int NULL);

        INSERT INTO dbo.tsitu1 (cdsituacao, dcsituacao, fldemissao) VALUES
            ('01','ATIVO','N'), ('02','AUXILIO DOENCA','N'), ('08','DEMITIDO','S'), ('09','TRANSFERIDO','N'), ('99','PRE CADASTRO','N');
        """;

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public ConnectionFactory Connections => new(ConnectionString);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.ExecuteAsync($"CREATE DATABASE {Database}");
        }

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = Database }.ConnectionString;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(RhuSchema);
    }

    /// <summary>Apaga os dados do RHSenso e o schema de estado entre testes.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync("""
            DELETE FROM dbo.feria2; DELETE FROM dbo.func1; DELETE FROM dbo.test1; DELETE FROM dbo.cargo1; DELETE FROM dbo.tcus1;
            IF OBJECT_ID('solidesdp.run_items') IS NOT NULL DROP TABLE solidesdp.run_items;
            IF OBJECT_ID('solidesdp.runs') IS NOT NULL DROP TABLE solidesdp.runs;
            IF OBJECT_ID('solidesdp.entity_state') IS NOT NULL DROP TABLE solidesdp.entity_state;
            IF OBJECT_ID('solidesdp.vacation_state') IS NOT NULL DROP TABLE solidesdp.vacation_state;
            IF OBJECT_ID('solidesdp.meta') IS NOT NULL DROP TABLE solidesdp.meta;
            """);
    }

    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return (await connection.QueryAsync<T>(sql, parameters)).AsList();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
