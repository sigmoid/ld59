
[ideation](#ideation)

* [4/27/26](#42726)
* [4/28/26](#42826)
* [4/29/26](#42926)

# Characters 

## Lawrence Pilgrim: Eutropia CEO
Wants: to make the world better

## Lance Nightingale: Anastasia CEO
Wants: power, validation

## Player
Employee of Lithos software

# Companies
## Eutropia: 
A mostly benevolent company who threatens to make actual institutional change
CEO: Lawrence Pilgrim

## Anastasia Corporation: 
A massive monopoly in control of the world
CEO: Lance Nightingale

## Lithos Software: 
A regular company caught in the crossfire. Flagship product is an operating system.
CEO: Ed Sled

## Scram!: 
A game company which is unwittingly used to further progress of the Anastasia corporation through acquisition
CEO: Rose cantrell

---

# Puzzles

## Tutorial

Player must identify the CEOs of each corporation. Completing this mission opens up the rest of the game

## Disaster

Project Codename: Lycurgus

Workers in an Anastasia corporation lithium mine are trapped in a cave. Anastasia leadership determines
that mounting a rescue effort is risky and choose to instead try to downplay the severity of the accident

Puzzle:
Identify the workers who died


---

## Theme is "Signal"

Virtual Desktop type of deal

Solve a mystery (similar to obra dinn)

Step one is building a virtual desktop

What kind of things are going to be on this desktop
* Hex viewer
* String viewer (ascii / utf8)
    * Player will start with files that guide them through decoding info
* Image viewer
    * Takes in Raw data
* Cypher decoder
    * Take in text only
* Translator
    * Takes in text only
* Decryptor (requires key)

---

### Gameplay could go like:

Player starts by reading some tutorial text in a text file

Player tries to decrypt simple text files

Text files give player cypher information / encryption keys

---

The player needs some other challenges than just finding cypher keys. 

* Data is stored in chunks which need to be reorganized

---

### Walking skeleton

Text / Hex viewer
Cypher text
Encryption

Player reads the tutorial, starts by figuring out how to decode simple text files, only the files are gibberish. Player somehow figures out how to decode one of these files


# Ideation
---

Need to figure out what's actually going on in this game.

I think the first phase is simply decoding basic text 

Maybe you have two files, one that's encoded and one that's not

There's a reference folder with items that might be useful. One of those items is something simple

---

alright so now I can encode a message by just swapping characters. The problem is not decoding this information without being too tedious
or boring. I think for starters it would make sense to have a program in the game where you can generate a solution without needing to 
manually fill in each replacement rule, but isn't that boring?

Also this solution needs to scale up as the rules get more complex

How does this thing work anyway?

There is a program. Player can set a file as input and a file as a key. Maybe later in the game players will have to do something to improve the key.

---

It'd be cool if there was some kind of minigame to decode certain things

Maybe you decode a file and it's coordinates and if you play those coordinates in minesweeper it unlocks a special mode with information

---

Okay brainblast

There are resources: names, occupatioins(?) and keys (and probably more later)
So you can unlock keys by comparing two pieces of information. THEN you can use the keys to decrypt stuff

Maybe you can use previous keys to improve them?

So the first thing is figuring out that the creed combined with the encrypted creed results in a key
After that I need to figure something out. Maybe using the key again results in some kind of partially scrambled output that you need another key for

So maybe when you decrypt a thing and it results in something that's still encrypted, so you have to find two matching documents again

---

Functionality is mostly built save for the mystery solving system and minesweeper so I'm thinking I need to actually come up with some content

What is this organization?

They're definitely evil somehow, and probably pretty stupid, but what are they doing? Drugs? Murder? Terrorism? Cyber crime?

Good name: Anastasia Corporation

Anastasia corporation is illegally doing something with people's data and you're trying to find proof of that.
Maybe they are illegally monitoring people. Maybe they are suspected in assasinating people

---

Need to come up with an actual puzzle

I like the idea of figuring out who oversees a project and finding its project codename, and for the third
piece of information you have to figure out who leaked to the press

Required documents

* Leaked news article
    * Identifies their source as a "Tier III" employee
* Project descriptions with codenames
* Org chart
* readme
* internal emails
    * Maybe referring to an employee by their initials


So I now have the information that the leaker was a tier iii employee which narrows down the search

I should elimnate some other tier III executives from suspicion

Some email saying "The tier II execs are overseeing codenames: x y z

--- 

I really want to get something playable done this week, but I keep coming up with ideas that aren't helping.

I like the concept of roottrees, building a family tree is a pretty good core system.

I think search functionality will probably be needed to make this game more fun, but I don't think I have time to do that in the two days that remain in this week.

Coming up with some novel mechanic that works is tough and I've been scratching my head all day thinking about it.

I think the narrative is pretty solid. You are investigating the Anastasia corporation, they had a CEO assasinated and you need to find out who's responsible.

I need to come up with a puzzle mechanic that makes the player feel smart without being lame.

Decoding sounds promising, but in practice I haven't really come up with anything to make it really fun.

Hyperlinks might be the easiest to implement way to get something fun happening.

I could just add a keyword search feature to the file explorer!

--- 

Okay so more thought has been put into this

I think decoding should stay

I think being able to keyword search in the file explorer is what will make this work

Player should have to find key files through deduction

So instead of how it works now, each file should just have a decrypted flag. If a file is not decrypted it shows up as nonsense.

--- 

Trying to design a puzzle that has multiple keys. I think what could be cool is if each key is encrypted with the base key
and then also some personally identifying information. So the player gets a bunch of personal things to different users and they
can combine that with the key to unlock more files. There should be lots of files to prevent brute force guessing.
These keys could be email addresses

So there's like a webmaster role that is one of the identities that you've found, and you have to deduce who the webmaster is to
find the right key combination.

As requested, here is a list of identities to consider for candidacy of key holder. Their personal data is stored in the folder "info"

---

## 4/27/26

I like the gameplay of the game okay but I feel the story is lacking. How can I make this story good?
I listened to a video about a diving disaster last night where the company in charge decided not to try to rescue
the divers due to liability. I like that idea as a subplot

I think one of the big problems currently is that the story doesn't introduce much conflict and pays off immediately
It would be better if the player feels like they are uncovering the start of a vast criminal underbelly of this organization

I like the story that the company had a ceo assassinated. Could use more detail though.

---

Just read some more Fear and Loathing on the campaign trail. Although i didn't really follow it, the section
on the convention was kinda exciting. I like the idea of a complex voting system getting totally fucked up.
In the book, McGovern staff 'shave' votes to make it seem like their losing because they know that if 
everyone else is focused on trivial challenges, they won't challenge their Ace, and that's how they secured
the nomination.

I think complicated voting rules would be really interesting to include in the game. People concealing votes,
making counterintuitive votes, etc.

Agenda: Call to order, roll call, minues, treasurers report, committee reports, unfinished business, new business, adjourn

motions requires a second

## 4/28/26

I think I need to come up with self contained cool mysteries.
Mining disaster, Political vote, assassination plot, secret society, Using games as secret communication

Story ideas
* Mining Disaster
* Political vote
* Assassination plot
* Secret Society
* Weather control
* Games as secret communication

Assassinated CEO was trying to do things the right way

I also like the idea of having a more open world approach. The player
can solve any mystery at any time, and mysteries are validated in sets of three or so

It'd be cool to introduce some kind of hard to believe element to the game

There needs to be a bigger running story that ties all of these things together. What could that be?
I mean for sure the elites are involved in some kind of fucked up war, but it would be cool if there 
was some element to this that sparked real intrigue

Each project codename is a different mystery and they can be solved in any order.

What really motivates elites? I think it probably always starts with some kind of insecurity
which leads them to want power. It could be that all these CEOs are rushing to find a fountain
of youth. Or they are trying to influence the world through proxies in media or something.

I mean everybody wants to be loved and to feel like they're a good person. What's interesting
is how that gets perverted to the point where you are willing to hurt other people to feel it.


## 4/29/26

Document ideas
* ID of a worker
* Project overview
    * map of the mine
* meeting minutes
* progress reports
    * promotion report  
    * blast schedule
    * report on mine safety
* phone call transcript

Key info
* Shaft was blocked
    * documents describing routine checks
    * timeline points to a check resulting in damage found
* One of the workers in the zone was recently promoted
    * the shift schedule doesn't perfectly match up to the info
* blasting didn't occur on the day of the incident
* roster for the mine
    * player needs to figure out where each person was
* detailed notes on the day's progress
* one worker was arrested (brother of one of the dead)

* Phone call transcript between two workers (project lead and the person at the mine) unencrypted, reveals encryption key
