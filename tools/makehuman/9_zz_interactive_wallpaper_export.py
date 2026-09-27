#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""One-shot MakeHuman exporter for the Interactive Wallpaper project.

The plugin remains inactive unless INTERACTIVE_WALLPAPER_MH_AUTO_EXPORT=1 is
present in MakeHuman's environment. It then creates a neutral semi-realistic
female test character, applies the Game Engine rig and exports FBX + MHM.
"""

import json
import os
import traceback

import gui3d
import getpath
import log
import material
import mh

_TIMER_ID = None


def _write_marker(directory, success, message, files=None):
    if not os.path.isdir(directory):
        os.makedirs(directory)
    marker = {
        "success": bool(success),
        "message": str(message),
        "files": files or [],
    }
    with open(os.path.join(directory, "makehuman-export-result.json"), "w", encoding="utf-8") as handle:
        json.dump(marker, handle, ensure_ascii=False, indent=2)


def _select_proxy(category_name, task_name, proxy_path):
    task = gui3d.app.getCategory(category_name).getTaskByName(task_name)
    if task is None:
        raise RuntimeError("MakeHuman task not found: %s/%s" % (category_name, task_name))
    if not os.path.isfile(proxy_path):
        raise RuntimeError("MakeHuman proxy not found: %s" % proxy_path)
    task.selectProxy(proxy_path)


def _configure_exporter():
    task = gui3d.app.getCategory("Files").getTaskByName("Export")
    if task is None:
        raise RuntimeError("MakeHuman FBX export task is unavailable")
    task.buildGui()
    exporter = task.getExporter("Filmbox (fbx)")
    if exporter is None:
        raise RuntimeError("MakeHuman FBX exporter is unavailable")

    exporter.feetOnGround.setChecked(True)
    exporter.binary.setChecked(True)
    exporter.hiddenGeom.setChecked(False)
    for button, name in task.scaleButtons:
        button.setChecked(name == "meter")
    return exporter


def _export_once():
    global _TIMER_ID
    if _TIMER_ID is not None:
        mh.removeTimer(_TIMER_ID)
        _TIMER_ID = None

    output_dir = os.environ.get("INTERACTIVE_WALLPAPER_MH_EXPORT_DIR", "").strip()
    if not output_dir:
        return
    output_dir = os.path.abspath(output_dir)

    try:
        human = gui3d.app.selectedHuman
        log.message("Interactive Wallpaper: configuring MakeHuman character")

        # A neutral young-adult, semi-realistic Asian female baseline.
        human.setGender(0.0)
        human.setAge(0.48)
        human.setWeight(0.52)
        human.setMuscle(0.38)
        human.setHeight(0.52)
        human.setAsian(0.72)
        human.applyAllTargets()

        system_data = getpath.getSysDataPath()

        skin = os.path.join(system_data, "skins", "young_asian_female", "young_asian_female.mhmat")
        if os.path.isfile(skin):
            human.material = material.fromFile(skin)

        _select_proxy("Geometries", "Hair", os.path.join(system_data, "hair", "bob01", "bob01.mhpxy"))
        _select_proxy("Geometries", "Clothes", os.path.join(system_data, "clothes", "female_casualsuit01", "female_casualsuit01.mhpxy"))
        _select_proxy("Geometries", "Clothes", os.path.join(system_data, "clothes", "shoes01", "shoes01.mhpxy"))

        skeleton_task = gui3d.app.getCategory("Pose/Animate").getTaskByName("Skeleton")
        if skeleton_task is None:
            raise RuntimeError("MakeHuman skeleton task is unavailable")
        skeleton_task.chooseSkeleton(os.path.join(system_data, "rigs", "game_engine.mhskel"))

        if not os.path.isdir(output_dir):
            os.makedirs(output_dir)
        model_base = "DefaultHuman"
        mhm_path = os.path.join(output_dir, model_base + ".mhm")
        human.save(mhm_path)

        exporter = _configure_exporter()

        def filename(extension, different=False):
            return os.path.join(output_dir, model_base + "." + extension)

        exporter.export(human, filename)
        fbx_path = os.path.join(output_dir, model_base + ".fbx")
        if not os.path.isfile(fbx_path):
            raise RuntimeError("MakeHuman did not create the expected FBX file")

        files = []
        for name in sorted(os.listdir(output_dir)):
            path = os.path.join(output_dir, name)
            if os.path.isfile(path):
                files.append(name)
        _write_marker(output_dir, True, "MakeHuman character exported", files)
        gui3d.app.status("Interactive Wallpaper character exported to %s", output_dir)
        log.message("Interactive Wallpaper: MakeHuman export completed: %s", fbx_path)
    except Exception as error:
        log.error("Interactive Wallpaper MakeHuman export failed", exc_info=True)
        try:
            _write_marker(output_dir, False, "%s\n%s" % (error, traceback.format_exc()))
        except Exception:
            pass


def load(app):
    global _TIMER_ID
    if os.environ.get("INTERACTIVE_WALLPAPER_MH_AUTO_EXPORT") != "1":
        return
    # User plugins load after the system exporter plugins. Delay once more so
    # all task views and the OpenGL scene have completed initialization.
    _TIMER_ID = mh.addTimer(5000, _export_once)


def unload(app):
    global _TIMER_ID
    if _TIMER_ID is not None:
        mh.removeTimer(_TIMER_ID)
        _TIMER_ID = None