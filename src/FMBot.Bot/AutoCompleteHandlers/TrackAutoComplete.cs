using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;

namespace FMBot.Bot.AutoCompleteHandlers;

public class TrackAutoComplete : AutocompleteHandler
{
    private readonly TrackService _trackService;

    public TrackAutoComplete(TrackService trackService)
    {
        this._trackService = trackService;
    }

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var recentlyPlayedTracks = await this._trackService.GetLatestTracks(context.User.Id);
        var recentTopAlbums = await this._trackService.GetRecentTopTracks(context.User.Id);

        var results = new List<string>();

        if (autocompleteInteraction?.Data?.Current?.Value == null ||
            string.IsNullOrWhiteSpace(autocompleteInteraction?.Data?.Current?.Value.ToString()))
        {
            if (recentlyPlayedTracks == null || !recentlyPlayedTracks.Any() ||
                recentTopAlbums == null || !recentTopAlbums.Any())
            {
                results.Add("Start typing to search through tracks...");

                return await Task.FromResult(
                    AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
            }

            results
                .ReplaceOrAddToList(recentlyPlayedTracks.Select(s => s.Name).Take(5));

            results
                .ReplaceOrAddToList(recentTopAlbums.Select(s => s.Name).Take(5));
        }
        else
        {
            try
            {
                var searchValue = autocompleteInteraction.Data.Current.Value.ToString();
                results = new List<string>
                {
                    searchValue
                };

                var trackResults =
                    await this._trackService.SearchThroughTracks(searchValue);

                results.ReplaceOrAddToList(recentlyPlayedTracks
                    .Where(w => w.Track.ToLower().StartsWith(searchValue.ToLower()))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentTopAlbums
                    .Where(w => w.Track.ToLower().StartsWith(searchValue.ToLower()))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentlyPlayedTracks
                    .Where(w => w.Track.ToLower().Contains(searchValue.ToLower()))
                    .Select(s => s.Name)
                    .Take(2));

                results.ReplaceOrAddToList(recentTopAlbums
                    .Where(w => w.Track.ToLower().Contains(searchValue.ToLower()))
                    .Select(s => s.Name)
                    .Take(3));

                results.ReplaceOrAddToList(trackResults
                    .Where(w => w.Artist.ToLower().StartsWith(searchValue.ToLower()))
                    .Take(2)
                    .Select(s => s.Name));

                results.ReplaceOrAddToList(trackResults
                    .Where(w => w.Popularity != null && w.Popularity > 60 &&
                                w.Name.ToLower().Contains(searchValue.ToLower()))
                    .Take(2)
                    .Select(s => s.Name));

                results.ReplaceOrAddToList(trackResults
                    .Where(w => w.Name.ToLower().StartsWith(searchValue.ToLower()))
                    .Take(4)
                    .Select(s => s.Name));


                results.ReplaceOrAddToList(trackResults
                    .Where(w => w.Name.ToLower().Contains(searchValue.ToLower()))
                    .Take(2)
                    .Select(s => s.Name));

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
    }
}
