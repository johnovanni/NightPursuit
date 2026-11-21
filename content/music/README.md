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

## Making a track louder or quieter

Instead of a plain name you can write an entry with a `volume`:

    "theme": { "file": "glass-canyon.mp3", "volume": 0.8 }

`1` leaves it as it is. `0.5` is half as loud. `2` is the most (twice as loud).
This works in `music.json` too.

## Good to know

- Use a plain file name only. No folders.
- If a named file is missing, the game shows a notice with its name when it starts. That track stays silent.
- The track files are committed to the repo with the game.
