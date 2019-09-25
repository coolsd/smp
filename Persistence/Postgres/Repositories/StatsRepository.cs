/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Dapper;
using MPS.Extensions;
using MPS.Persistence.Model;
using MPS.Persistence.Model.Projections;
using MPS.Persistence.Repositories;
using MPS.Time;
using NLog;
using MinerStats = MPS.Persistence.Model.Projections.MinerStats;

namespace MPS.Persistence.Postgres.Repositories
{
    public class StatsRepository : IStatsRepository
    {
        public StatsRepository(IMapper mapper, IMasterClock clock)
        {
            this.mapper = mapper;
            this.clock = clock;
        }

        private readonly IMapper mapper;
        private readonly IMasterClock clock;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan MinerStatsMaxAge = TimeSpan.FromMinutes(20);

        public async Task InsertPoolStatsAsync(IDbConnection con, IDbTransaction tx, PoolStats stats)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.PoolStats>(stats);

            const string query = "INSERT INTO poolstats(poolid, connectedminers, poolhashrate, networkhashrate, " +
                "networkdifficulty, lastnetworkblocktime, blockheight, connectedpeers, sharespersecond, created) " +
                "VALUES(@poolid, @connectedminers, @poolhashrate, @networkhashrate, @networkdifficulty, " +
                "@lastnetworkblocktime, @blockheight, @connectedpeers, @sharespersecond, @created)";

            await con.ExecuteAsync(query, mapped, tx);
        }

        public async Task InsertMinerWorkerPerformanceStatsAsync(IDbConnection con, IDbTransaction tx, MinerWorkerPerformanceStats stats)
        {
            logger.LogInvoke();

            var mapped = mapper.Map<Entities.MinerWorkerPerformanceStats>(stats);

            if(string.IsNullOrEmpty(mapped.Worker))
                mapped.Worker = string.Empty;

            const string query = "INSERT INTO minerstats(poolid, miner, worker, hashrate, sharespersecond, created) " +
                "VALUES(@poolid, @miner, @worker, @hashrate, @sharespersecond, @created)";

            await con.ExecuteAsync(query, mapped, tx);
        }

        public async Task<PoolStats> GetLastPoolStatsAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            const string query = "SELECT * FROM poolstats WHERE poolid = @poolId ORDER BY created DESC FETCH NEXT 1 ROWS ONLY";

            var entity = await con.QuerySingleOrDefaultAsync<Entities.PoolStats>(query, new { poolId });
            if(entity == null)
                return null;

            return mapper.Map<PoolStats>(entity);
        }

        public Task<decimal> GetTotalPoolPaymentsAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            const string query = "SELECT sum(amount) FROM payments WHERE poolid = @poolId";

            return con.ExecuteScalarAsync<decimal>(query, new { poolId });
        }

        public async Task<PoolStats[]> GetPoolPerformanceBetweenAsync(IDbConnection con, string poolId, SampleInterval interval, DateTime start, DateTime end)
        {
            logger.LogInvoke(new[] { poolId });

            string trunc = null;

            switch(interval)
            {
                case SampleInterval.Hour:
                    trunc = "hour";
                    break;

                case SampleInterval.Day:
                    trunc = "day";
                    break;
            }

            var query = $"SELECT date_trunc('{trunc}', created) AS created, " +
                "AVG(poolhashrate) AS poolhashrate, AVG(networkhashrate) AS networkhashrate, AVG(networkdifficulty) AS networkdifficulty, " +
                "CAST(AVG(connectedminers) AS BIGINT) AS connectedminers " +
                "FROM poolstats " +
                "WHERE poolid = @poolId AND created >= @start AND created <= @end " +
                $"GROUP BY date_trunc('{trunc}', created) " +
                "ORDER BY created;";

            return (await con.QueryAsync<Entities.PoolStats>(query, new { poolId, start, end }))
                .Select(mapper.Map<PoolStats>)
                .ToArray();
        }

        public async Task<MinerStats> GetMinerStatsAsync(IDbConnection con, IDbTransaction tx, string poolId, string miner)
        {
            logger.LogInvoke(new[] { poolId, miner });

            var query = "SELECT (SELECT SUM(difficulty) FROM shares WHERE poolid = @poolId AND miner = @miner) AS pendingshares, " +
                "(SELECT amount FROM balances WHERE poolid = @poolId AND address = @miner) AS pendingbalance, " +
                "(SELECT SUM(amount) FROM payments WHERE poolid = @poolId and address = @miner) as totalpaid," +
                "(SELECT SUM(amount) FROM payments WHERE poolid = @poolId and address = @miner and created >= date_trunc('day', now())) as todaypaid";

            var result = await con.QuerySingleOrDefaultAsync<MinerStats>(query, new { poolId, miner }, tx);

            if(result != null)
            {
                query = "SELECT * FROM payments WHERE poolid = @poolId AND address = @miner" +
                    " ORDER BY created DESC LIMIT 1";

                result.LastPayment = await con.QuerySingleOrDefaultAsync<Payment>(query, new { poolId, miner }, tx);

                // query timestamp of last stats update
                query = "SELECT created FROM minerstats WHERE poolid = @poolId AND miner = @miner" +
                    " ORDER BY created DESC LIMIT 1";

                var lastUpdate = await con.QuerySingleOrDefaultAsync<DateTime?>(query, new { poolId, miner }, tx);

                // ignore stale minerstats
                if(lastUpdate.HasValue && (clock.Now - DateTime.SpecifyKind(lastUpdate.Value, DateTimeKind.Utc) > MinerStatsMaxAge))
                    lastUpdate = null;

                if(lastUpdate.HasValue)
                {
                    // load rows rows by timestamp
                    query = "SELECT * FROM minerstats WHERE poolid = @poolId AND miner = @miner AND created = @created";

                    var stats = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, created = lastUpdate }))
                        .Select(mapper.Map<MinerWorkerPerformanceStats>)
                        .ToArray();

                    if(stats.Any())
                    {
                        // replace null worker with empty string
                        foreach(var stat in stats)
                        {
                            if(stat.Worker == null)
                            {
                                stat.Worker = string.Empty;
                                break;
                            }
                        }

                        // transform to dictionary
                        result.Performance = new WorkerPerformanceStatsContainer
                        {
                            Workers = stats.ToDictionary(x => x.Worker ?? string.Empty, x => new WorkerPerformanceStats
                            {
                                Hashrate = x.Hashrate,
                                SharesPerSecond = x.SharesPerSecond
                            }),

                            Created = stats.First().Created
                        };
                    }
                }
            }

            return result;
        }

        public async Task<MinerWorkerHashrate[]> GetPoolMinerWorkerHashratesAsync(IDbConnection con, string poolId)
        {
            logger.LogInvoke();

            const string query =
                "SELECT s.miner, s.worker, s.hashrate FROM " +
                "(" +
                "    WITH cte AS" +
                "    (" +
                "        SELECT" +
                "            ROW_NUMBER() OVER (partition BY miner, worker ORDER BY created DESC) as rk," +
                "            miner, worker, hashrate" +
                "        FROM minerstats" +
                "        WHERE poolid = @poolId" +
                "    )" +
                "    SELECT miner, worker, hashrate" +
                "    FROM cte" +
                "    WHERE rk = 1" +
                ") s " +
                "WHERE s.hashrate > 0;";

            return (await con.QueryAsync<MinerWorkerHashrate>(query, new { poolId }))
                .ToArray();
        }

        public async Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenHourlyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new[] { poolId });

            const string query = "SELECT worker, date_trunc('hour', created) AS created, AVG(hashrate) AS hashrate, " +
                "AVG(sharespersecond) AS sharespersecond FROM minerstats " +
                "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                "GROUP BY date_trunc('hour', created), worker " +
                "ORDER BY created, worker;";

            var entities = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end }))
                .ToArray();

            // ensure worker is not null
            foreach(var entity in entities)
                entity.Worker = entity.Worker ?? string.Empty;

            // group
            var entitiesByDate = entities
                .GroupBy(x => x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker ?? string.Empty, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
                .ToArray();
            //.ToDictionary(x=> x.Created.ToUniversalTime().ToUnixTimestamp(), x=> x);

            //// fill in blanks
            //var result = new List<WorkerPerformanceStatsContainer>();

            //for (var i = 0; i < 24; i++)
            //{
            //    if(tmp.TryGetValue(end.ToUnixTimestamp(), out var item))
            //        result.Insert(0, item);
            //    else
            //        result.Add(new WorkerPerformanceStatsContainer { Created = end, Workers = new Dictionary<string, WorkerPerformanceStats>() });

            //    end = end.AddHours(-1);
            //}

            //return result.ToArray();
            return tmp;
        }

        public async Task<WorkerPerformanceStatsContainer[]> GetMinerPerformanceBetweenDailyAsync(IDbConnection con, string poolId, string miner, DateTime start, DateTime end)
        {
            logger.LogInvoke(new[] { poolId });

            const string query = "SELECT worker, date_trunc('day', created) AS created, AVG(hashrate) AS hashrate, " +
                "AVG(sharespersecond) AS sharespersecond FROM minerstats " +
                "WHERE poolid = @poolId AND miner = @miner AND created >= @start AND created <= @end " +
                "GROUP BY date_trunc('day', created), worker " +
                "ORDER BY created, worker;";

            var entitiesByDate = (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, miner, start, end }))
                .ToArray()
                .GroupBy(x => x.Created);

            var tmp = entitiesByDate.Select(x => new WorkerPerformanceStatsContainer
            {
                Created = x.Key,
                Workers = x.ToDictionary(y => y.Worker, y => new WorkerPerformanceStats
                {
                    Hashrate = y.Hashrate,
                    SharesPerSecond = y.SharesPerSecond
                })
            })
                .ToArray();
            //.ToDictionary(x => x.Created.ToUniversalTime().ToUnixTimestamp(), x => x);

            //// fill in blanks
            //var result = new List<WorkerPerformanceStatsContainer>();

            //for (var i = 0; i < 30; i++)
            //{
            //    if (tmp.TryGetValue(end.ToUnixTimestamp(), out var item))
            //        result.Insert(0, item);
            //    else
            //        result.Add(new WorkerPerformanceStatsContainer { Created = end, Workers = new Dictionary<string, WorkerPerformanceStats>() });

            //    end = end.AddDays(-1);
            //}

            //return result.ToArray();
            return tmp;
        }

        public async Task<MinerWorkerPerformanceStats[]> PagePoolMinersByHashrateAsync(IDbConnection con, string poolId, DateTime from, int page, int pageSize)
        {
            logger.LogInvoke(new[] { (object) poolId, from, page, pageSize });

            const string query = "WITH tmp AS " +
                "( " +
                "	SELECT  " +
                "		ms.miner,  " +
                "		ms.hashrate,  " +
                "		ms.sharespersecond,  " +
                "		ROW_NUMBER() OVER(PARTITION BY ms.miner ORDER BY ms.hashrate DESC) AS rk  " +
                "	FROM (SELECT miner, SUM(hashrate) AS hashrate, SUM(sharespersecond) AS sharespersecond " +
                "       FROM minerstats " +
                "       WHERE poolid = @poolid AND created >= @from GROUP BY miner, created) ms " +
                ") " +
                "SELECT t.miner, t.hashrate, t.sharespersecond " +
                "FROM tmp t " +
                "WHERE t.rk = 1 " +
                "ORDER by t.hashrate DESC " +
                "OFFSET @offset FETCH NEXT (@pageSize) ROWS ONLY";

            return (await con.QueryAsync<Entities.MinerWorkerPerformanceStats>(query, new { poolId, from, offset = page * pageSize, pageSize }))
                .Select(mapper.Map<MinerWorkerPerformanceStats>)
                .ToArray();
        }

        public Task<int> DeletePoolStatsBeforeAsync(IDbConnection con, DateTime date)
        {
            logger.LogInvoke();

            const string query = "DELETE FROM poolstats WHERE created < @date";

            return con.ExecuteAsync(query, new { date });
        }

        public Task<int> DeleteMinerStatsBeforeAsync(IDbConnection con, DateTime date)
        {
            logger.LogInvoke();

            const string query = "DELETE FROM minerstats WHERE created < @date";

            return con.ExecuteAsync(query, new { date });
        }
    }
}
