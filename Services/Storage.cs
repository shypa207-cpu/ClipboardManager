using System;
using System.Collections.Generic;
using System.IO;
using ClipboardManager.Models;
using Microsoft.Data.Sqlite;

namespace ClipboardManager.Services;

public sealed class Storage
{
    private const int PreviewChars = 600;
    private readonly string _cs;
    private readonly object _lock = new();

    public Storage()
    {
        _cs = new SqliteConnectionStringBuilder { DataSource = AppPaths.Db, Pooling = true }.ToString();
        lock (_lock)
        {
            using var c = Open();
            Exec(c, "PRAGMA journal_mode=WAL;");
            Exec(c, @"CREATE TABLE IF NOT EXISTS items(
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        kind INTEGER NOT NULL,
                        text TEXT,
                        image_path TEXT,
                        thumb_path TEXT,
                        hash TEXT NOT NULL UNIQUE,
                        pinned INTEGER NOT NULL DEFAULT 0,
                        created INTEGER NOT NULL);");
        }
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public List<ClipItem> GetAll()
    {
        var list = new List<ClipItem>();
        lock (_lock)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"SELECT id, kind, substr(text,1,$n), image_path, thumb_path, hash, pinned, created
                                FROM items ORDER BY pinned DESC, created DESC;";
            cmd.Parameters.AddWithValue("$n", PreviewChars);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new ClipItem
                {
                    Id = r.GetInt64(0),
                    Kind = (ClipKind)r.GetInt32(1),
                    Text = r.IsDBNull(2) ? null : r.GetString(2),
                    ImagePath = r.IsDBNull(3) ? null : r.GetString(3),
                    ThumbPath = r.IsDBNull(4) ? null : r.GetString(4),
                    Hash = r.GetString(5),
                    IsPinned = r.GetInt32(6) != 0,
                    CreatedUtc = DateTime.FromBinary(r.GetInt64(7))
                });
            }
        }
        return list;
    }

    public string? GetFullText(long id)
    {
        lock (_lock)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT text FROM items WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteScalar() as string;
        }
    }

    /// <summary>Додає елемент (або піднімає наверх, якщо такий вже є) і обрізає історію до ліміту.</summary>
    public void Add(ClipItem item, int limit)
    {
        lock (_lock)
        {
            using var c = Open();
            using (var up = c.CreateCommand())
            {
                up.CommandText = "UPDATE items SET created=$t WHERE hash=$h;";
                up.Parameters.AddWithValue("$t", DateTime.UtcNow.ToBinary());
                up.Parameters.AddWithValue("$h", item.Hash);
                if (up.ExecuteNonQuery() == 0)
                {
                    using var ins = c.CreateCommand();
                    ins.CommandText = @"INSERT INTO items(kind,text,image_path,thumb_path,hash,pinned,created)
                                        VALUES($k,$t,$ip,$tp,$h,0,$c);";
                    ins.Parameters.AddWithValue("$k", (int)item.Kind);
                    ins.Parameters.AddWithValue("$t", (object?)item.Text ?? DBNull.Value);
                    ins.Parameters.AddWithValue("$ip", (object?)item.ImagePath ?? DBNull.Value);
                    ins.Parameters.AddWithValue("$tp", (object?)item.ThumbPath ?? DBNull.Value);
                    ins.Parameters.AddWithValue("$h", item.Hash);
                    ins.Parameters.AddWithValue("$c", DateTime.UtcNow.ToBinary());
                    ins.ExecuteNonQuery();
                }
            }
            TrimLocked(c, limit);
        }
    }

    public void Trim(int limit)
    {
        lock (_lock) { using var c = Open(); TrimLocked(c, limit); }
    }

    private void TrimLocked(SqliteConnection c, int limit)
    {
        var victims = new List<(long Id, string? Img, string? Thumb)>();
        using (var q = c.CreateCommand())
        {
            q.CommandText = @"SELECT id, image_path, thumb_path FROM items WHERE pinned=0
                              ORDER BY created DESC LIMIT -1 OFFSET $l;";
            q.Parameters.AddWithValue("$l", Math.Max(1, limit));
            using var r = q.ExecuteReader();
            while (r.Read())
                victims.Add((r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        }
        foreach (var v in victims)
        {
            using var d = c.CreateCommand();
            d.CommandText = "DELETE FROM items WHERE id=$id;";
            d.Parameters.AddWithValue("$id", v.Id);
            d.ExecuteNonQuery();
            DeleteFiles(v.Img, v.Thumb);
        }
    }

    public void SetPinned(long id, bool pinned)
    {
        lock (_lock)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE items SET pinned=$p WHERE id=$id;";
            cmd.Parameters.AddWithValue("$p", pinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(long id)
    {
        lock (_lock)
        {
            using var c = Open();
            string? img = null, thumb = null;
            using (var q = c.CreateCommand())
            {
                q.CommandText = "SELECT image_path, thumb_path FROM items WHERE id=$id;";
                q.Parameters.AddWithValue("$id", id);
                using var r = q.ExecuteReader();
                if (r.Read())
                {
                    img = r.IsDBNull(0) ? null : r.GetString(0);
                    thumb = r.IsDBNull(1) ? null : r.GetString(1);
                }
            }
            using var d = c.CreateCommand();
            d.CommandText = "DELETE FROM items WHERE id=$id;";
            d.Parameters.AddWithValue("$id", id);
            d.ExecuteNonQuery();
            DeleteFiles(img, thumb);
        }
    }

    public void ClearUnpinned()
    {
        lock (_lock)
        {
            using var c = Open();
            var files = new List<(string?, string?)>();
            using (var q = c.CreateCommand())
            {
                q.CommandText = "SELECT image_path, thumb_path FROM items WHERE pinned=0;";
                using var r = q.ExecuteReader();
                while (r.Read())
                    files.Add((r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
            }
            Exec(c, "DELETE FROM items WHERE pinned=0;");
            foreach (var (i, t) in files) DeleteFiles(i, t);
        }
    }

    private static void DeleteFiles(params string?[] paths)
    {
        foreach (var p in paths)
        {
            try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { AppPaths.LogError("Delete file", ex); }
        }
    }
}
