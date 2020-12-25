using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services.Guild
{
    public class GuildService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        private readonly Regex _mentionRegex = new Regex(@"[^<>@]");

        public GuildService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        // Message is in dm?
        public bool CheckIfDM(ICommandContext context)
        {
            return context.Guild == null;
        }

        public async Task<Persistence.Domain.Models.Guild> GetGuildAsync(ulong guildId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Guilds
                .AsQueryable()
                .Include(i => i.GuildBlockedUsers)
                    .ThenInclude(t => t.User)
                .Include(i => i.GuildUsers)
                    .ThenInclude(t => t.User)
                .Include(i => i.Channels)
                .Include(i => i.Webhooks)
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guildId);
        }

        public List<GuildUser> FilterGuildUsersAsync(Persistence.Domain.Models.Guild guild)
        {
            var guildUsers = guild.GuildUsers.ToList();
            if (guild.ActivityThresholdDays.HasValue)
            {
                guildUsers = guildUsers.Where(w =>
                    w.User.LastUsed != null &&
                    w.User.LastUsed >= DateTime.UtcNow.AddDays(-guild.ActivityThresholdDays.Value))
                    .ToList();
            }
            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromWhoKnows))
            {
                guildUsers = guildUsers.Where(w =>
                    !guild.GuildBlockedUsers
                        .Where(wh => wh.BlockedFromWhoKnows)
                        .Select(s => s.UserId).Contains(w.UserId))
                    .ToList();
            }

            return guildUsers.ToList();
        }

        // Get user from guild with ID
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, ulong id)
        {
            return await context.Guild.GetUserAsync(id);
        }

        public async Task<GuildPermissions> CheckSufficientPermissionsAsync(ICommandContext context)
        {
            var user = await context.Guild.GetUserAsync(context.Client.CurrentUser.Id);
            return user.GuildPermissions;
        }

        // Get user from guild with searchvalue
        public async Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, string searchValue)
        {
            if (searchValue.Length > 3 && this._mentionRegex.IsMatch(searchValue))
            {
                var id = searchValue.Trim('@', '!', '<', '>');

                if (ulong.TryParse(id, out var discordUserId))
                {
                    return await context.Guild.GetUserAsync(discordUserId);
                }
            }

            return null;
        }

        public async Task<GuildUser> GetUserFromGuild(Persistence.Domain.Models.Guild guild, int userId)
        {
            return guild.GuildUsers
                .FirstOrDefault(f => f.UserId == userId);
        }


        // Get last.fm username from guild with searchvalue
        public async Task<string> MentionToLastFmUsernameAsync(string searchValue)
        {
            if (searchValue.Length > 3)
            {
                if (this._mentionRegex.IsMatch(searchValue))
                {
                    var id = searchValue.Trim('@', '!', '<', '>');

                    if (ulong.TryParse(id, out ulong discordUserId))
                    {
                        await using var db = this._contextFactory.CreateDbContext();
                        var userSettings = await db.Users
                            .AsQueryable()
                            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

                        return userSettings?.UserNameLastFM;
                    }
                }

                return searchValue;
            }

            return null;
        }


        // Get user from guild with searchvalue
        public async Task<User> MentionToUserAsync(string searchValue)
        {
            if (searchValue.Length > 3)
            {
                if (this._mentionRegex.IsMatch(searchValue))
                {
                    var id = searchValue.Trim('@', '!', '<', '>');

                    if (ulong.TryParse(id, out ulong discordUserId))
                    {
                        await using var db = this._contextFactory.CreateDbContext();
                        var userSettings = await db.Users
                            .AsQueryable()
                            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

                        return userSettings;
                    }
                }
            }

            return null;
        }

        // Get all guild users
        public async Task<List<UserExportModel>> FindAllUsersFromGuildAsync(ICommandContext context)
        {
            var users = await context.Guild.GetUsersAsync();

            var userIds = users.Select(s => s.Id).ToList();

            await using var db = this._contextFactory.CreateDbContext();
            var usersObject = db.Users
                .AsQueryable()
                .Where(w => userIds.Contains(w.DiscordUserId))
                .Select(s =>
                    new UserExportModel(
                        s.DiscordUserId.ToString(),
                        s.UserNameLastFM));

            return usersObject.ToList();
        }

        public async Task ChangeGuildSettingAsync(IGuild guild, ChartTimePeriod chartTimePeriod, FmEmbedType fmEmbedType)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = guild.Id,
                    ChartTimePeriod = chartTimePeriod,
                    FmEmbedType = fmEmbedType,
                    Name = guild.Name,
                    TitlesEnabled = true
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();
            }
        }

        public async Task SetGuildReactionsAsync(IGuild guild, string[] reactions)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    EmoteReactions = reactions,
                    Name = guild.Name
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.EmoteReactions = reactions;
                existingGuild.Name = guild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }
        }

        public async Task<bool?> ToggleSupporterMessagesAsync(IGuild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    DisableSupporterMessages = true
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                return true;
            }
            else
            {
                existingGuild.Name = guild.Name;
                if (existingGuild.DisableSupporterMessages == true)
                {
                    existingGuild.DisableSupporterMessages = false;
                }
                else
                {
                    existingGuild.DisableSupporterMessages = true;
                }

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();

                return existingGuild.DisableSupporterMessages;
            }
        }

        public async Task<bool?> ToggleCrownsAsync(IGuild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstAsync(f => f.DiscordGuildId == guild.Id);

            existingGuild.Name = guild.Name;

            if (existingGuild.CrownsDisabled == true)
            {
                existingGuild.CrownsDisabled = false;
            }
            else
            {
                existingGuild.CrownsDisabled = true;
            }

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return existingGuild.CrownsDisabled;
        }

        public async Task<bool> SetWhoKnowsActivityThresholdDaysAsync(IGuild guild, int? days)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            existingGuild.Name = guild.Name;
            existingGuild.ActivityThresholdDays = days;
            existingGuild.CrownsActivityThresholdDays = days;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> SetCrownActivityThresholdDaysAsync(IGuild guild, int? days)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            existingGuild.Name = guild.Name;
            existingGuild.CrownsActivityThresholdDays = days;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> SetMinimumCrownPlaycountThresholdAsync(IGuild guild, int? playcount)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            existingGuild.Name = guild.Name;
            existingGuild.CrownsMinimumPlaycountThreshold = playcount;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return true;
        }

        public async Task<bool> BlockGuildUserAsync(IGuild guild, int userId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            var existingBlockedUser = await db.GuildBlockedUsers
                .AsQueryable()
                .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

            if (existingBlockedUser != null)
            {
                existingBlockedUser.BlockedFromWhoKnows = true;
                existingBlockedUser.BlockedFromCrowns = true;

                db.Entry(existingBlockedUser).State = EntityState.Modified;

                await db.SaveChangesAsync();

                return true;
            }

            var blockedGuildUserToAdd = new GuildBlockedUser
            {
                GuildId = existingGuild.GuildId,
                UserId = userId,
                BlockedFromCrowns = true,
                BlockedFromWhoKnows = true
            };

            await db.GuildBlockedUsers.AddAsync(blockedGuildUserToAdd);
            await db.SaveChangesAsync();

            db.Entry(blockedGuildUserToAdd).State = EntityState.Detached;

            Log.Information("Added blocked user {userId} to guild {guildName}", userId, guild.Name);

            return true;
        }

        public async Task<bool> CrownBlockGuildUserAsync(IGuild guild, int userId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            var existingBlockedUser = await db.GuildBlockedUsers
                .AsQueryable()
                .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

            if (existingBlockedUser != null)
            {
                existingBlockedUser.BlockedFromCrowns = true;

                db.Entry(existingBlockedUser).State = EntityState.Modified;

                await db.SaveChangesAsync();

                return true;
            }

            var blockedGuildUserToAdd = new GuildBlockedUser
            {
                GuildId = existingGuild.GuildId,
                UserId = userId,
                BlockedFromCrowns = true
            };

            await db.GuildBlockedUsers.AddAsync(blockedGuildUserToAdd);
            await db.SaveChangesAsync();

            db.Entry(blockedGuildUserToAdd).State = EntityState.Detached;

            Log.Information("Added crownblocked user {userId} to guild {guildName}", userId, guild.Name);

            return true;
        }

        public async Task<bool> UnBlockGuildUserAsync(IGuild guild, int userId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                return false;
            }

            var existingBlockedUser = await db.GuildBlockedUsers
                .AsQueryable()
                .FirstOrDefaultAsync(a => a.GuildId == existingGuild.GuildId && a.UserId == userId);

            if (existingBlockedUser == null)
            {
                return true;
            }

            db.GuildBlockedUsers.Remove(existingBlockedUser);
            await db.SaveChangesAsync();

            Log.Information("Removed blocked user {userId} from guild {guildName}", userId, guild.Name);

            return true;
        }

        public async Task SetGuildPrefixAsync(IGuild guild, string prefix)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    Prefix = prefix
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.Prefix = prefix;
                existingGuild.Name = guild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }
        }

        public async Task<string[]> GetDisabledCommandsForGuild(IGuild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            return existingGuild?.DisabledCommands;
        }

        public async Task<string[]> AddGuildDisabledCommandAsync(IGuild guild, string command)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = guild.Id,
                    TitlesEnabled = true,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    DisabledCommands = new[] { command }
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();

                return newGuild.DisabledCommands;
            }

            if (existingGuild.DisabledCommands != null && existingGuild.DisabledCommands.Length > 0)
            {
                var newDisabledCommands = existingGuild.DisabledCommands;
                Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + 1);
                newDisabledCommands[^1] = command;
                existingGuild.DisabledCommands = newDisabledCommands;
            }
            else
            {
                existingGuild.DisabledCommands = new[] { command };
            }

            existingGuild.Name = guild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return existingGuild.DisabledCommands;
        }

        public async Task<string[]> RemoveGuildDisabledCommandAsync(IGuild guild, string command)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            existingGuild.DisabledCommands = existingGuild.DisabledCommands.Where(w => !w.Contains(command)).ToArray();

            existingGuild.Name = guild.Name;

            db.Entry(existingGuild).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return existingGuild.DisabledCommands;
        }

        public async Task<string[]> GetDisabledCommandsForChannel(IChannel channel)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == channel.Id);

            return existingGuild?.DisabledCommands;
        }

        public async Task<string[]> AddChannelDisabledCommandAsync(IChannel channel, int guildId, string command)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingChannel = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == channel.Id);

            if (existingChannel == null)
            {
                var newChannel = new Channel
                {
                    DiscordChannelId = channel.Id,
                    Name = channel.Name,
                    GuildId = guildId,
                    DisabledCommands = new[] { command }
                };

                await db.Channels.AddAsync(newChannel);

                await db.SaveChangesAsync();

                return newChannel.DisabledCommands;
            }

            if (existingChannel.DisabledCommands != null && existingChannel.DisabledCommands.Length > 0)
            {
                var newDisabledCommands = existingChannel.DisabledCommands;
                Array.Resize(ref newDisabledCommands, newDisabledCommands.Length + 1);
                newDisabledCommands[^1] = command;
                existingChannel.DisabledCommands = newDisabledCommands;
            }
            else
            {
                existingChannel.DisabledCommands = new[] { command };
            }

            existingChannel.Name = existingChannel.Name;

            db.Entry(existingChannel).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return existingChannel.DisabledCommands;
        }

        public async Task<string[]> RemoveChannelDisabledCommandAsync(IChannel channel, string command)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingChannel = await db.Channels
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordChannelId == channel.Id);

            existingChannel.DisabledCommands = existingChannel.DisabledCommands.Where(w => !w.Contains(command)).ToArray();

            existingChannel.Name = channel.Name;

            db.Entry(existingChannel).State = EntityState.Modified;

            await db.SaveChangesAsync();

            return existingChannel.DisabledCommands;
        }

        public async Task<DateTime?> GetGuildIndexTimestampAsync(IGuild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            return existingGuild?.LastIndexed;
        }

        public async Task UpdateGuildIndexTimestampAsync(IGuild guild, DateTime? timestamp = null)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var existingGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (existingGuild == null)
            {
                var newGuild = new Persistence.Domain.Models.Guild
                {
                    DiscordGuildId = guild.Id,
                    ChartTimePeriod = ChartTimePeriod.Monthly,
                    FmEmbedType = FmEmbedType.embedmini,
                    Name = guild.Name,
                    TitlesEnabled = true,
                    LastIndexed = timestamp ?? DateTime.UtcNow
                };

                await db.Guilds.AddAsync(newGuild);

                await db.SaveChangesAsync();
            }
            else
            {
                existingGuild.LastIndexed = timestamp ?? DateTime.UtcNow;
                existingGuild.Name = guild.Name;

                db.Entry(existingGuild).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }
        }

        public bool ValidateReactions(string[] emoteString)
        {
            foreach (var emote in emoteString)
            {
                if (emote.Length == 2)
                {
                    try
                    {
                        var unused = new Emoji(emote);
                    }
                    catch
                    {
                        return false;
                    }
                }
                else
                {
                    try
                    {
                        Emote.Parse(emote);
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        public async Task AddReactionsAsync(IUserMessage message, IGuild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var dbGuild = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (dbGuild?.EmoteReactions == null || !dbGuild.EmoteReactions.Any())
            {
                return;
            }

            foreach (var emoteString in dbGuild.EmoteReactions)
            {
                if (emoteString.Length == 2)
                {
                    var emote = new Emoji(emoteString);
                    await message.AddReactionAsync(emote);
                }
                else
                {
                    var emote = Emote.Parse(emoteString);
                    await message.AddReactionAsync(emote);
                }
            }
        }

        public async Task AddGuildAsync(SocketGuild guild)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = guild.Id,
                ChartTimePeriod = ChartTimePeriod.Monthly,
                FmEmbedType = FmEmbedType.embedmini,
                Name = guild.Name,
                TitlesEnabled = true
            };

            await using var db = this._contextFactory.CreateDbContext();
            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();
        }

        public async Task RemoveGuildAsync(SocketGuild guild)
        {
            var newGuild = new Persistence.Domain.Models.Guild
            {
                DiscordGuildId = guild.Id,
                ChartTimePeriod = ChartTimePeriod.Monthly,
                FmEmbedType = FmEmbedType.embedmini,
                Name = guild.Name,
                TitlesEnabled = true
            };

            await using var db = this._contextFactory.CreateDbContext();
            await db.Guilds.AddAsync(newGuild);

            await db.SaveChangesAsync();
        }

        public async Task<bool> GuildExistsAsync(SocketGuild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id) != null;
        }

        public async Task<int> GetTotalGuildCountAsync()
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Guilds
                .AsQueryable()
                .CountAsync();
        }
    }
}