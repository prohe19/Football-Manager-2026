# FM26 Property IDs (the scouting map)

Extracted live from `PropertyIdentifierSet` (8,233 properties total) via the mod's
registry dumper. These are the `uint` PropertyIDs we pass to the binding system to
read a person's data. **This is the core reference for the whole scouting feature.**

> Game build: 26.3.2 (Steam App 3551340, build 23583635). IDs may shift on big patches;
> re-run the dumper to refresh.

## ⭐ The headline numbers (CA / PA / reputation)

### Players
| PropertyID | Name |
|---|---|
| **1346584898** | **PlayerCurrentAbility** (CA) |
| **1347436866** | **PlayerPotentialAbility** (PA) |
| 1212512568 | PerceivedPotentialAbility |
| 1146252104 | PlayerCurrentReputation |
| 1346916944 | PlayerHomeReputation |
| 1347899984 | PlayerWorldReputation |

### Non-players (staff / managers)
| PropertyID | Name |
|---|---|
| **862020186** | **NonPlayerCurrentAbility** |
| **1647325216** | **NonPlayerPotentialAbility** |
| 1769628246 | NonPlayerCurrentReputation |
| 1481198386 | NonPlayerHomeReputation |
| 1667525201 | NonPlayerWorldReputation |
| 1970362724 | ManagerReputation |

### Scouted / star-rating views (what the UI shows without cheating)
| PropertyID | Name |
|---|---|
| 844321568 | CurrentAbilityStars |
| 1815509588 | CurrentAbilityScore |
| 1480150644 | PotentialAbilityStars |
| 1480679788 | PotentialAbilityScore |
| 1131757922 | CurrentAbilityStarRange |
| 1349468514 | PotentialAbilityStarRange |
| 2036486263 | ScoutedCurrentAbilityInfo |
| 1399683185 | ScoutedPotentialAbilityInfo |

## Identity / basics
| PropertyID | Name |
|---|---|
| 1718186862 | FirstName |
| 1936024430 | SecondName |
| 1951683927 | MiddleName |
| 843789105 | Surname |
| 825565216 | Age |
| 1348694389 | DateOfBirth |
| 1349481321 | Position |
| 1349481322 | Role |
| 1349481281 | PositionalAbilities |
| 1885696627 | Person |
| 1886157170 | Player |
| 862938733 | IsPlayer |
| 1517962082 | IsNonPlayer |

## Technical / mental / physical attributes (the 1–20 stats)
| ID | Name | | ID | Name |
|---|---|---|---|---|
| 858923040 | AttributeFinishing | | 876159008 | AttributeDetermination |
| 858857504 | AttributeDribbling | | 876093472 | AttributeDecisions |
| 858791968 | AttributeCrossing | | 876027936 | AttributeConcentration |
| 875634720 | AttributeTackling | | 875962400 | AttributeComposure |
| 859381792 | AttributePassing | | 875896864 | AttributeBravery |
| 875700256 | AttributeTechnique | | 875831328 | AttributeAnticipation |
| 858988576 | AttributeFirstTouch | | 875765792 | AttributeAggression |
| 859119648 | AttributeHeading | | 892346400 | AttributeFlair |
| 859185184 | AttributeLongShots | | 892411936 | AttributeLeadership |
| 859054112 | AttributeFreeKicks | | 892477472 | AttributeMovement (off the ball) |
| 875569184 | AttributePenaltyTaking | | 892543008 | AttributePositioning |
| 842604576 | AttributeCorners | | 892608544 | AttributeTeamwork |
| 859250720 | AttributeLongThrows | | 892674080 | AttributeVision |
| 859316256 | AttributeMarking | | 892739616 | AttributeWorkRate |
| 909254688 | AttributePace | | 892805152 | AttributeAcceleration |
| 892870688 | AttributeAgility | | 892936224 | AttributeBalance |
| 909123616 | AttributeJumpingReach | | 909189152 | AttributeNaturalFitness |
| 909320224 | AttributeStamina | | 909385760 | AttributeStrength |

### Goalkeeper attributes
| ID | Name |
|---|---|
| 926167088 | AttributeOneOnOnes |
| 926101552 | AttributeThrowing |
| 926036016 | AttributeTendencyToPunch |
| 925970480 | AttributeRushingOut |
| 925966369 | AttributeReflexes |
| 925900832 | AttributeKicking |
| 909713441 | AttributeHandling |
| 909647905 | AttributeEccentricity |
| 909582369 | AttributeCommunication |
| 909516833 | AttributeCommandOfArea |
| 926232624 | AttributeAerialReach |

## Staff / coaching attributes (for "top staff by role")
| PropertyID | Name |
|---|---|
| 842151456 | JudgingPlayerAbility |
| 842151712 | JudgingPlayerPotential |
| 842151968 | JudgingStaffAbility |
| 842215456 | TacticalKnowledge |
| 842152224 | Negotiating |
| 825831968 | Technical (coaching) |
| 825831712 | Tactical |
| 825832224 | Possession |
| 825831456 | GoalkeepingCoaching |
| 825767712 | Fitness |
| 825767456 | Defending |
| 825767200 | Attacking |
| 842019104 | Motivating |
| 842018848 | PeopleManagement |
| 808798786 | AttributesCoaching |
| 912811842 | AttributesMedical |
| 1517375600 | AttributesScouting |

## Personality / hidden
| PropertyID | Name |
|---|---|
| 1349742196 | Personality |
| 1349742703 | AttributeSportsmanship |
| 1349546607 | AttributeProfessionalism |
| 1348562274 | AttributeAmbition |
| 1349283705 | AttributeLoyalty |
| 1348757874 | Dirtiness |
| 1349087346 | InjuryProneness |
| 1346588494 | Consistency |
| 1349936498 | Versatility |
| 1348559969 | AttributeAdaptability |

## What's next

We have the IDs. To turn them into a scouting list we still need to:
1. Get a specific person's **reference / ReferenceID** (a real player from the save).
2. **Read a typed value** for `(ReferenceID, PropertyID)` via the `SI.Bindable` binding system.
3. **Enumerate all persons** (the "all players/staff" query).
4. Rank: Top players by CA, wonderkids by PA + age, staff by role attributes.

The full 8,233-property dump is reproducible any time by launching with the mod
(`ScoutUI` auto-dumps to the BepInEx console / LogOutput.log).
