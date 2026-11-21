# Music for Night Pursuit

Put your music files in this folder (`content/music`). Use WAV or MP3.
Keep each file under 100 MB, because GitHub refuses bigger files.
The game plays each track over and over, so make the end join cleanly to the start.

## Screen music

Open `music.json` and put the file name between the quote marks:

    { "title": "", "briefing": "briefing.mp3", "results": "results.mp3", "gameOver": "", "finished": "" }

- `title`: the title screen. If empty, the `briefing` track plays.
- `briefing`: every mission briefing. If empty, there is no music.
- `results`: when a mission is complete.
- `gameOver`: if empty, the `results` track plays.
- `finished`: when the whole campaign is done. If empty, the `results` track plays.

Leaving a slot as `""` is fine. Nothing plays and there is no warning.

## Mission music

Each mission's own file (in `content/missions`) can name its tunes. Add this block:

    "music": { "theme": "glass-canyon.mp3", "boss": "red-lantern.mp3" }

- `theme` plays during the mission.
- `boss` takes over when the boss appears. If empty, the theme carries on.

Put the block just before the last `}` of the mission file. The line before it needs a comma at the end.
The end of a mission file then looks like this:

    "crates": [ ...your crates... ],
    "music": { "theme": "glass-canyon.mp3", "boss": "red-lantern.mp3" }
    }

(The last line before the closing brace has no comma after it.)

A mistake in a mission file's `music` block (a wrong field name, a file name with a folder in it, a `volume` out of range)
stops that mission from loading, and the error box names the mission file and the field.
Fix it and start the game again.

## Making a track louder or quieter

Instead of a plain name you can write an entry with a `volume`:

    "theme": { "file": "glass-canyon.mp3", "volume": 0.8 }

`1` leaves it as it is. `0.5` is half as loud. `2` is the most (twice as loud),
but a loud track can distort at 2, so try a lower number first.
This works in `music.json` too.

## Good to know

- Use a plain file name only. No folders. It must end in `.mp3` or `.wav`.
- The slot names in `music.json` can be written in any case (`gameOver`, `GAMEOVER` and `GameOver` all work).
- If a named file is missing, the game shows a notice with its name when it starts. That track stays silent.
- If something in `music.json` is wrong, a notice names the slot and says what is wrong,
  for example `MUSIC LIST: RESULTS VOLUME MUST BE FROM 0 TO 2`. Only that slot is silent. The others still play.
- If `music.json` has a typing mistake the game cannot follow, a notice says `MUSIC LIST COULD NOT BE READ`
  and gives the line number. No screen music plays until it is fixed.
- A comma after the last entry, and `//` notes, are fine in `music.json`.
- The track files are committed to the repo with the game.
