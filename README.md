# Night Pursuit (working title)

A midnight vector-graphics car-combat game for Windows (C#, WPF): first-person driving, a radar map,
spy-style weapons, and missions that end with a boss car.

Status: early. `spikes/ControlsLab` is a throwaway prototype for choosing the controls; see its
`SPIKE-NOTES.md`. Requirements, architecture and tests come next.

## In play

When you beat a boss, its car crashes off the road and blows up before the results: a spin-off, a roll or a flip,
different each time. Enter (or Y on a gamepad) skips straight to the results.

A boss ahead of you never slows below 90 mph to stay in your sights. Hang back below 45 mph and it does something about
it: makes a run for it, shoots back, drops oil or caltrops, or backs into you. Later bosses react sooner and choose
better. If it gets more than 600 m ahead, "BOSS GETTING AWAY" counts down 20 seconds (or it is gone at 1,200 m): the
mission fails and starts again from its briefing, you keep your points and lose a spare car, and with none left the
game is over. Every 25,000 points earns an extra car (up to 5 spare cars).

## Releases

Push a version tag (for example `v0.1.0`) and `.github/workflows/release.yml` tests and publishes the game,
signs `NightPursuit.exe` and its own DLLs as John Apolloni with Azure Artifact Signing, builds
`NightPursuit-Setup.exe` from `packaging/installer/NightPursuit.iss` and signs it too, then attaches the
installer and a portable zip (each with a SHA-256 file) to a GitHub Release.

The installer puts the game in `C:\Program Files\Qapow\NightPursuit` by default, and the player can pick
another folder. Settings and high scores stay in `%AppData%\NightPursuit`, so they survive updates and uninstalls.

Pull requests that touch the code or packaging build the same files unsigned and upload them as a workflow artifact.
