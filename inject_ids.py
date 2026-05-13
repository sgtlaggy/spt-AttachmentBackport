#!/usr/bin/env python3

import json
import sys
from argparse import ArgumentParser
from pathlib import Path
from typing import Any, TypedDict

HERE = Path(__file__).parent
MODS = HERE.parent
SPT = MODS.parent.parent

PARSER = ArgumentParser(
    description="Add IDs to data.\nExpects to be run in an installed version of the mod with WTT Content Backport installed by default."
)
PARSER.add_argument(
    "-d", "--delete", help="Delete items that can't be found.", action="store_true"
)
PARSER.add_argument(
    "-s",
    "--spt",
    help="Path to SPT en.json",
    type=Path,
    default=SPT / "SPT_Data" / "database" / "locales" / "global" / "en.json",
)
PARSER.add_argument(
    "-w",
    "--wtt",
    help="Path to WTT en.json",
    type=Path,
    default=MODS / "WTT-ContentBackport" / "db" / "CustomLocales" / "en.json",
)
ARGS = PARSER.parse_args()

SPT_EN = ARGS.spt
WTT_EN = ARGS.wtt
CHANGES = HERE / "Data" / "attachmentChanges.json"


NAME_REWRITES = {
    "AK 7.62x39 US Palm AK30 30-round magazine (Black)": 'AK 7.62x39 US Palm "AK30" 30-round magazine (Black)',
    "AK 7.62x39 US Palm AK30 30-round magazine (FDE)": 'AK 7.62x39 US Palm "AK30" 30-round magazine (FDE)',
    "TheAKGuy AK-50 .50 BMG anti-materiel rifle": "TheAKGuy AK-50 .50 BMG sniper rifle",
    "AR-10 2A Armament X3 7.62x51 compensator": "AR-10 2A Armanent X3 7.62x51 compensator",
}


class Changes(TypedDict):
    tpl: str
    name: str
    changes: dict[str, Any]


class AttachmentChanges(TypedDict):
    items: list[Changes]


def main():
    loc = load_locales()
    changes: AttachmentChanges = load_json(CHANGES)
    remove: list[int] = []

    for index, item in enumerate(changes["items"]):
        name = item["name"]
        mongo = loc.get(NAME_REWRITES.get(name, name))
        if mongo is None:
            print(f"WARNING: ID not found for {name}")
            remove.append(index)
        else:
            item["tpl"] = mongo

    if ARGS.delete:
        for index in remove[::-1]:
            changes["items"].pop(index)

    CHANGES.copy(CHANGES.with_suffix(".json.bak"))
    CHANGES.write_text(json.dumps(changes, indent=2))


def load_locales() -> dict[str, str]:
    name_to_id: dict[str, str] = {}

    loc: dict[str, str] = {}
    try:
        loc.update(load_json(SPT_EN))
    except FileNotFoundError:
        print("SPT locale not found.")
        hang()
    except Exception as e:
        print(e)
        hang()

    try:
        loc.update(load_json(WTT_EN))
    except FileNotFoundError:
        print("WTT locale not found.")
        hang()
    except Exception as e:
        print(e)
        hang()

    for key, value in loc.items():
        mongo, _, name = key.partition(" ")
        if name != "Name":
            continue

        if value in name_to_id:
            print(f"WARNING: Duplicate ‘{value}’: {name_to_id[value]}, {mongo}")  # noqa: RUF001

        name_to_id[value] = mongo

    return name_to_id


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def hang():
    # no args provided, likely double-clicked
    if not sys.argv[1:]:
        input("Press RETURN to exit.")
    sys.exit()


if __name__ == "__main__":
    main()
