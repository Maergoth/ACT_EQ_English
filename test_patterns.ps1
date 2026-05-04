$f="c:\Users\ryan\Desktop\EQ1 ACT Plugin\eq1log_Waer.txt"
$tail = '\.?(?:\s*\((?<spec>[^)]+)\))?\s*$'
$rules = [ordered]@{
  PlayerSpellDD = '^You hit (?<vic>.+?) for (?<n>[\d,]+) points? of (?<dmgtype>\w+) damage by (?<spell>.+?)\.(?:\s*\((?<spec>[^)]+)\))?$'
  NpcSpellDD    = '^(?<atk>.+?) hit (?<vic>.+?) for (?<n>[\d,]+) points? of (?<dmgtype>\w+) damage by (?<spell>.+?)\.(?:\s*\((?<spec>[^)]+)\))?$'
  DoTTick       = '^(?<vic>.+?) has taken (?<n>[\d,]+) damage from your (?<spell>.+?)\.(?:\s*\((?<spec>[^)]+)\))?$'
  DoTTickByOther= '^(?<vic>.+?) has taken (?<n>[\d,]+) damage by (?<spell>.+?)\.(?:\s*\((?<spec>[^)]+)\))?$'
  YouTookDoT    = '^(?<vic>.+?) (?:have|has) taken (?<n>[\d,]+) damage from (?<spell>.+?) by (?<atk>.+?)\.(?:\s*\((?<spec>[^)]+)\))?$'
  DamageShield  = "^(?<vic>.+?) (?:is|are) (?<verb>\w+) by (?:(?<atk>.+?)'s|YOUR) (?<src>[\w ]+?) for (?<n>[\d,]+) points? of non-melee damage[.!](?:\s*\((?<spec>[^)]+)\))?$"
  PlayerFrenzy  = '^You (?<verb>frenzy) on (?<vic>.+?) for (?<n>[\d,]+) points? of damage' + $tail
  PlayerMelee   = '^You (?<verb>\w+) (?<vic>.+?) for (?<n>[\d,]+) points? of damage' + $tail
  NpcMelee      = '^(?<atk>.+?) (?<verb>hits|kicks|cleaves|slashes|pierces|bashes|claws|bites|crushes|slams|stabs|mauls|rends|smites|gores|smashes|frenzies|slices|stings|sweeps|slaps|gnaws|chomps|strikes|punches|backstabs|shoots) (?:on )?(?<vic>.+?) for (?<n>[\d,]+) points? of damage' + $tail
  PlayerMiss    = '^You try to (?<verb>\w+) (?:on )?(?<vic>.+?), but (?<why>.+?)!?\.?(?:\s*\((?<spec>[^)]+)\))?$'
  NpcMiss       = '^(?<atk>.+?) tries to (?<verb>\w+) (?:on )?(?<vic>.+?), but (?<why>.+?)!?\.?(?:\s*\((?<spec>[^)]+)\))?$'
  HealOnYou     = '^(?<atk>.+?) healed (?<vic>you|YOU)(?<hot> over time)? for (?<n>[\d,]+) (?:\([\d,]+\) )?hit points? by (?<spell>.+?)\.$'
  HealSelf      = '^(?<atk>.+?) healed (?<refl>himself|herself|itself|themself|themselves)(?<hot> over time)? for (?<n>[\d,]+) (?:\([\d,]+\) )?hit points? by (?<spell>.+?)' + $tail
  HealOther     = '^(?<atk>.+?) healed (?<vic>(?!himself|herself|itself|themself|themselves|you|YOU\b).+?)(?<hot> over time)? for (?<n>[\d,]+) (?:\([\d,]+\) )?hit points? by (?<spell>.+?)' + $tail
  Mend          = '^You (?:magically )?mend your wounds and heal (?:some|considerable|all of your) damage\.$'
  Rune          = '^You gain a rune for (?<n>[\d,]+) points? of absorption\.$'
  PlayerSlay    = '^You have slain (?<vic>.+?)!\.?$'
  PlayerDeath   = '^You have been slain by (?<atk>.+?)!\.?$'
  OtherSlain    = '^(?<vic>.+?) has been slain by (?<atk>.+?)!\.?$'
  ZoneEnter     = '^You have entered (?<zone>.+?)\.?$'
  DSAbsorbed    = "^(?:(?<vic>.+?)'s|YOUR) magical skin absorbs the damage of (?:(?<atk>.+?)'s|YOUR) (?<src>[\w ]+?)\.$"
  NonMeleeHit   = '^You were hit by non-melee for (?<n>[\d,]+) damage\.$'
  SpellResist   = "^(?<vic>.+?) resisted (?:your|(?<atk>.+?)'s) (?<spell>.+?)!$"
  YouDied       = '^You died\.$'
  Taunt         = "^(?<atk>.+?) (?:has captured|capture|was partially successful in capturing) (?<vic>.+?)'s attention\b.*[.!]$"
}
$counts=@{}; foreach($k in $rules.Keys){ $counts[$k]=0 }
$total=0; $combatish=0; $unmatchedSamples=@()
foreach($line in [IO.File]::ReadLines($f)){
  $total++
  if($line.Length -lt 27 -or $line[0] -ne '['){ continue }
  $body=$line.Substring(27)
  $isCombat = $body -match 'damage|healed|slain|tr(y|ies) to |has taken|rune for|have entered|mend your wounds|resisted .+!$|^You died\.$|capturing .+attention|captured .+attention'
  if(-not $isCombat){ continue }
  $combatish++
  $hit=$false
  foreach($k in $rules.Keys){ if($body -match $rules[$k]){ $counts[$k]++; $hit=$true; break } }
  if(-not $hit -and $unmatchedSamples.Count -lt 25){ $unmatchedSamples += $body }
}
"Total lines: $total"
"Combat-shaped lines: $combatish"
$counts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "{0,-15} {1}" -f $_.Key,$_.Value }
$matched=($counts.Values|Measure-Object -Sum).Sum
"Matched (any rule): $matched"
""
"--- Unmatched samples ---"
$unmatchedSamples | ForEach-Object { $_ }
