#!/usr/bin/env python3
"""Build isolated desktop/iOS releases, then publish explicitly via ff-api. No commits/deploys."""
import argparse
import base64
import hashlib
import http.client
import time
import xml.etree.ElementTree as ET
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile
import plistlib

TARGETS = {"desktop-web": "WebGL", "ios": "iOS", "linux-server": "StandaloneLinux64"}
IDENTITY = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$")
PATH_PART = re.compile(r"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,239}$")
UNITY = Path("/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity")

def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")

def safe_id(value):
    return bool(IDENTITY.fullmatch(value)) and ".." not in value

def safe_path(value):
    return bool(value) and len(value) <= 240 and not any(c in value for c in "\\?#%:\x00\r\n") and str(PurePosixPath(value)) == value and all(PATH_PART.fullmatch(p) and ".." not in p for p in value.split("/"))

def file_digest(path):
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(chunk)
    return sha.hexdigest()

def isolated(path):
    path = path.resolve()
    marker = path / ".vv-disposable-project.json"
    if not marker.is_file() or any((path / name).exists() for name in (".git", ".plastic")):
        raise ValueError("Build workers must be marked disposable projects without SCM metadata")
    source = Path(json.loads(marker.read_text())["sourceProject"]).resolve()
    if source == path or not (source / "Assets").is_dir():
        raise ValueError("Disposable marker must identify a separate source project")
    return path

def create_worker(args):
    source, destination = args.source.resolve(), args.destination.resolve()
    if destination.exists() or source == destination or source in destination.parents and destination.relative_to(source).parts[0] != "Builds":
        raise ValueError("Use a new worker path outside authoring Assets/Packages/ProjectSettings")
    if not all((source / name).is_dir() for name in ("Assets", "Packages", "ProjectSettings")):
        raise ValueError("Source must be a Unity project")
    sdk_root = getattr(args, "sdk_root", None)
    if sdk_root and json.loads((sdk_root.resolve() / "package.json").read_text()).get("name") != "com.virtualvenues.sdk":
        raise ValueError("SDK root must identify com.virtualvenues.sdk")
    destination.mkdir(parents=True)
    for name in ("Assets", "Packages", "ProjectSettings"):
        # Copy only project inputs. SCM metadata, builds and compiler caches stay at their source.
        shutil.copytree(source / name, destination / name, ignore=shutil.ignore_patterns(".git", ".plastic", ".DS_Store"))
    # Materialize the running SDK when it lives in PackageCache/Packages rather than Assets.
    # A creator must build with the same SDK code they are authoring against.
    sdk_root = getattr(args, "sdk_root", None)
    if sdk_root:
        sdk_root = sdk_root.resolve()
        package = json.loads((sdk_root / "package.json").read_text())
        if package.get("name") != "com.virtualvenues.sdk":
            raise ValueError("SDK root must identify com.virtualvenues.sdk")
        if source / "Assets" not in sdk_root.parents:
            embedded = destination / "Packages" / "com.virtualvenues.sdk"
            if embedded.exists():
                shutil.rmtree(embedded)
            shutil.copytree(sdk_root, embedded, ignore=shutil.ignore_patterns(".git", ".plastic", ".DS_Store", "__pycache__"))
    # Relative file: dependencies otherwise point at the disposable copy's parents.
    manifest_path = destination / "Packages" / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    for key, value in manifest.get("dependencies", {}).items():
        if value.startswith("file:"):
            local = Path(value[5:])
            if not local.is_absolute():
                manifest["dependencies"][key] = "file:" + (source / "Packages" / local).resolve().as_posix()
    write(manifest_path, manifest)
    write(destination / ".vv-disposable-project.json", {"purpose": "Desktop/native platform publishing", "sourceProject": str(source), "preserveSourceGuids": True})
    print("Created isolated worker " + str(destination))

def new_output(path):
    path = path.resolve()
    if path.exists() and any(path.iterdir()):
        raise ValueError("Use a new empty output directory")
    path.mkdir(parents=True, exist_ok=True)
    return path

def guard_output(path, workers):
    path = path.resolve()
    for worker in workers:
        if worker is None:
            continue
        worker = isolated(worker)
        source = Path(json.loads((worker / ".vv-disposable-project.json").read_text())["sourceProject"]).resolve()
        if path == worker or worker in path.parents or path == source or (source in path.parents and path.relative_to(source).parts[0] != "Builds"):
            raise ValueError("Keep outputs outside workers and authoring inputs; use the source Builds folder or an external folder")

def run_unity(unity, project, target, method, log, env):
    subprocess.run([str(unity), "-batchmode", "-quit", "-projectPath", str(project), "-buildTarget", target,
                    "-executeMethod", method, "-logFile", str(log)], check=True, env=env)

def collect_variant(folder, platform, kind, unity_version=None, profile=None):
    folder = folder.resolve()
    evidence = folder / "platform-build.json"
    if not evidence.is_file():
        raise ValueError("Missing platform-build.json; build with the target-aware worker first")
    result = json.loads(evidence.read_text())
    if result.get("result") != "Succeeded" or result.get("target") != TARGETS[platform] or result.get("platform") != platform:
        raise ValueError("Build did not succeed for the selected platform")
    entry = result["entryPoint"]
    if kind == "world" and platform == "linux-server":
        names = [entry]
    elif kind == "world":
        manifest = json.loads((folder / entry).read_text())
        if manifest.get("target") != TARGETS[platform]:
            raise ValueError("World manifest target mismatch")
        names = [entry] + [b["file"] for b in manifest["bundles"]]
        audit = json.loads((folder / "native-validation.json").read_text())
        if audit.get("result") != "passed":
            raise ValueError("World release validation failed")
    elif kind in ("catalog", "asset-pack"):
        names = [p.name for p in folder.iterdir() if p.suffix in (".bin", ".hash", ".bundle")]
    elif kind == "player" and platform == "ios":
        names = [entry]
    else:
        names = [p.relative_to(folder).as_posix() for p in folder.rglob("*") if p.is_file() and p.name != "platform-build.json"
                 and p.suffix not in (".meta", ".log") and p.name != ".DS_Store"]
    files = []
    for name in sorted(set(names)):
        if not safe_path(name):
            raise ValueError("Unsafe artifact path: " + name)
        path = folder / name
        if path.is_symlink() or not path.is_file() or folder not in path.resolve().parents:
            raise ValueError("Artifact must be a regular file within its target folder")
        if kind == "world" and platform != "linux-server" and name != entry:
            declared = next(b for b in manifest["bundles"] if b["file"] == name)
            if path.stat().st_size != declared["bytes"]:
                raise ValueError("World bundle length differs from manifest")
        files.append({"path": name, "bytes": path.stat().st_size, "sha256": file_digest(path)})
    variant={"platform": platform, "target": TARGETS[platform], "profile": profile or result.get("profile", "source-render-config"),
            "unityVersion": unity_version or result["unityVersion"], "entryPoint": entry, "files": files}
    if platform=="ios" and kind=="player" and "capabilities" in result: variant["capabilities"]=result["capabilities"]
    return variant

def manifest_for(args, output, folders):
    release = {"schemaVersion": 1, "releaseId": args.release_id or str(uuid.uuid4()), "assetId": args.asset_id,
               "kind": args.kind, "version": args.version, "runtimeVersion": args.runtime_version, "variants": []}
    for key in ("releaseId", "assetId", "version", "runtimeVersion"):
        if not safe_id(release[key]):
            raise ValueError("Invalid " + key)
    for platform, folder in folders.items():
        release["variants"].append(collect_variant(folder, platform, args.kind, profile="native-mobile-v1" if args.core_profile and platform == "ios" else None))
    validate_manifest(release)
    write(output / "release.json", release)
    write(output / "local-sources.json", {p: str(f.resolve()) for p, f in folders.items()})
    return release

def build_content(args):
    guard_output(args.output, [args.project_desktop, args.project_ios, args.project_server])
    output = new_output(args.output)
    job = json.loads(args.job.read_text())
    if job.get("kind") not in ("world", "catalog", "asset-pack"):
        raise ValueError("Job kind must be world, catalog, or asset-pack")
    args.kind = job["kind"]
    args.release_id = args.release_id or str(uuid.uuid4())
    folders = {}
    for platform, worker in (("desktop-web", args.project_desktop), ("ios", args.project_ios), ("linux-server", args.project_server)):
        if worker is None:
            continue
        worker = isolated(worker)
        folder = output / platform
        request = {**job, "platform": platform, "outputDirectory": str(folder), "releaseId": args.release_id}
        if args.kind in ("catalog", "asset-pack"):
            if not args.content_origin or not args.owner_id:
                raise ValueError("Catalog builds require --content-origin and --owner-id for final remote bundle URLs")
            origin = urllib.parse.urlparse(args.content_origin)
            if origin.scheme != "https" or not origin.netloc or origin.query or origin.fragment or origin.username:
                raise ValueError("Use a non-secret HTTPS content origin")
            owner = urllib.parse.quote(args.owner_id, safe="")
            request["contentBaseUrl"] = args.content_origin.rstrip("/") + f"/users/{owner}/platform-releases/{args.kind}/{args.asset_id}/{args.release_id}/{platform}"
        request_path = output / (platform + "-request.json")
        write(request_path, request)
        env = os.environ.copy()
        env.pop("VV_PUBLISH_TOKEN", None)
        env["VV_PLATFORM_CONTENT_REQUEST"] = str(request_path)
        method = "VirtualVenuesBuildUploader.PlatformContentProfileBuild.RunBatch" if args.core_profile else "VirtualVenues.Editor.Publishing.PlatformContentBuild.RunBatch"
        run_unity(args.unity, worker, TARGETS[platform], method, output / (platform + "-unity.log"), env)
        folders[platform] = folder
    if not folders:
        raise ValueError("Select at least one isolated desktop or iOS worker")
    return manifest_for(args, output, folders)

def validate_manifest(release):
    if release.get("schemaVersion") != 1 or any(not safe_id(release.get(key, "")) for key in ("releaseId", "assetId", "version", "runtimeVersion")):
        raise ValueError("Invalid release identity/version")
    kind = release.get("kind")
    if kind not in ("world", "catalog", "asset-pack", "player"):
        raise ValueError("Unsupported release kind")
    variants = release.get("variants", [])
    if not 1 <= len(variants) <= 3:
        raise ValueError("Select one to three platform variants")
    platforms, file_count = set(), 0
    for variant in variants:
        platform, entry = variant["platform"], variant["entryPoint"]
        if "capabilities" in variant:
            caps=variant["capabilities"]
            if kind!="player" or platform!="ios" or not isinstance(caps,list) or len(caps)>32 or any(not isinstance(c,str) or not re.fullmatch(r"[a-z0-9][a-z0-9._-]{0,79}",c) or ".." in c for c in caps) or len(set(caps))!=len(caps):
                raise ValueError("Invalid native player capabilities")
        if platform in platforms or platform not in TARGETS or variant["target"] != TARGETS[platform] or (platform == "linux-server" and kind != "world"):
            raise ValueError("Duplicate platform or platform/build target mismatch")
        platforms.add(platform)
        if not safe_id(variant["profile"]) or not safe_id(variant["unityVersion"]) or not safe_path(entry):
            raise ValueError("Invalid platform variant")
        paths = set()
        for file in variant["files"]:
            if not safe_path(file["path"]) or file["path"] in paths or not isinstance(file["bytes"], int) or not 0 < file["bytes"] <= 5 * 1024**3 or not re.fullmatch(r"[0-9a-f]{64}", file["sha256"]):
                raise ValueError("Invalid/duplicate file or SHA-256")
            paths.add(file["path"])
            file_count += 1
        if entry not in paths:
            raise ValueError("Entry point must name an uploaded file")
        if kind == "world" and platform != "linux-server" and entry != "world.streaming.json":
            raise ValueError("World clients require world.streaming.json")
        if kind in ("catalog", "asset-pack") and (entry != "content_catalog.bin" or "content_catalog.hash" not in paths):
            raise ValueError("Content requires catalog .bin and .hash")
        if kind == "player" and ((platform == "desktop-web" and entry != "index.html") or (platform == "ios" and not entry.endswith(".zip"))):
            raise ValueError("Player requires index.html or an iOS Xcode export zip")
    if file_count > 512:
        raise ValueError("Release exceeds 512 files")

def check_release(release, sources):
    validate_manifest(release)
    for variant in release["variants"]:
        root = Path(sources[variant["platform"]]).resolve()
        for file in variant["files"]:
            if not safe_path(file["path"]):
                raise ValueError("Unsafe upload path")
            path = root / file["path"]
            if not path.is_file() or path.is_symlink() or root not in path.resolve().parents or path.stat().st_size != file["bytes"] or file_digest(path) != file["sha256"]:
                raise ValueError("Release changed since preparation: " + file["path"])
        if release["kind"]=="player" and variant["platform"]=="ios":
            check_native_player_identity(root/variant["entryPoint"],release,variant)

def check_native_player_identity(path,release,variant):
    # Old archives remain usable as historical backups. New identity-bearing
    # archives must prove the same version/release/runtime/features as the API row.
    try:
        with zipfile.ZipFile(path) as archive:
            name="Data/Raw/VV_UPCIdentity.json"
            if name not in archive.namelist():
                if "capabilities" in variant: raise ValueError("Native player archive is missing its UPC identity")
                return
            if archive.namelist().count(name)!=1 or archive.namelist().count("Info.plist")!=1:
                raise ValueError("Duplicate native identity metadata in the archive")
            if archive.getinfo(name).file_size>16384 or archive.getinfo("Info.plist").file_size>262144:
                raise ValueError("Native identity metadata exceeds its limit")
            identity=json.loads(archive.read(name))
            host=plistlib.loads(archive.read("Info.plist")).get("FutureFestUPC")
            expected={"schemaVersion":1,"upcVersion":release["version"],"upcReleaseId":release["releaseId"],
                "runtimeVersion":release["runtimeVersion"],"unityVersion":variant["unityVersion"],"capabilities":variant.get("capabilities",[])}
            if not isinstance(identity,dict) or not isinstance(host,dict) or type(identity.get("schemaVersion")) is not int or type(host.get("schemaVersion")) is not int or identity!=expected or host!=identity:
                raise ValueError("Native archive UPC identity differs from its registered release")
    except (zipfile.BadZipFile,KeyError,plistlib.InvalidFileException,json.JSONDecodeError) as error:
        raise ValueError("Invalid native player archive/identity: "+str(error)) from None

class APIError(ValueError):
    def __init__(self, code, message):
        self.code = code
        super().__init__(message)


class UploadHTTPError(ValueError):
    def __init__(self, status, body):
        self.status = status
        code = ""
        try:
            root = ET.fromstring(body)
            value = root.findtext("Code", "")
            if re.fullmatch(r"[A-Za-z0-9]{1,80}", value): code = " " + value
            message = root.findtext("Message", "").lower()
            for phrase, label in [("headers present in the request which were not signed", "unsigned request headers"), ("no identity-based policy allows", "missing identity permission"), ("explicit deny", "explicit policy denial"), ("request has expired", "expired upload URL"), ("request time", "request clock mismatch")]:
                if phrase in message: code += " (" + label + ")"; break
            unsigned = root.findtext("HeadersNotSigned", "")
            if re.fullmatch(r"[a-zA-Z0-9;, -]{1,160}", unsigned): code += " [" + unsigned + "]"
        except ET.ParseError:
            # A bounded read can truncate a verbose signature error response.
            match = re.search(rb"<Code>([A-Za-z0-9]{1,80})</Code>", body)
            if match: code = " " + match.group(1).decode("ascii")
        super().__init__("HTTP " + str(status) + code)


def upload_once(url, path, headers):
    uri = urllib.parse.urlsplit(url)
    if uri.scheme != "https" or not uri.hostname or uri.username or uri.password or uri.fragment:
        raise ValueError("Upload URL must use HTTPS without embedded account credentials")
    query = urllib.parse.parse_qs(uri.query)
    headers = dict(headers)
    for name in list(headers):
        if name.lower().startswith("x-amz-") and name.lower() in {key.lower() for key in query}:
            values = next(value for key, value in query.items() if key.lower() == name.lower())
            if values != [headers[name]]: raise ValueError("Upload query/header checksum mismatch")
            raise ValueError("Publisher API update required: checksum must be a signed request header, not a query parameter; retry this export after deploying the signer fix")
    # Preserve configured proxy behavior. Reopen the stream for each retry in either path.
    if urllib.request.getproxies().get("https") and not urllib.request.proxy_bypass(uri.hostname):
        with path.open("rb") as source:
            request = urllib.request.Request(url, data=source, method="PUT", headers=headers)
            try:
                with urllib.request.urlopen(request, timeout=120) as response: response.read(1024)
            except urllib.error.HTTPError as error:
                raise UploadHTTPError(error.code, error.read(8192)) from None
        return
    connection = http.client.HTTPSConnection(uri.hostname, uri.port or 443, timeout=120, blocksize=1024*1024)
    try:
        with path.open("rb") as source:
            try:
                connection.request("PUT", urllib.parse.urlunsplit(("", "", uri.path or "/", uri.query, "")), body=source, headers=headers)
            except OSError as error:
                # Storage can reject headers before a large body finishes sending. Read
                # that response instead of losing its status behind a broken-pipe error.
                try: response = connection.getresponse()
                except (OSError, http.client.HTTPException): raise error from None
            else:
                response = connection.getresponse()
            body = response.read(8192)
            if not 200 <= response.status < 300:
                raise UploadHTTPError(response.status, body)
    finally:
        connection.close()


def upload_file(url, path, headers, label):
    for attempt in range(1, 4):
        print(f"Uploading {label}, attempt {attempt}/3", flush=True)
        try:
            upload_once(url, path, headers)
            return
        except UploadHTTPError as error:
            if error.status not in (408, 429) and error.status < 500:
                raise ValueError(f"Upload failed for {label}: {error}; release remains a draft") from None
            reason = str(error)
        except (OSError, urllib.error.URLError, http.client.HTTPException):
            reason = "connection interrupted"
        if attempt == 3:
            raise ValueError(f"Upload failed for {label}: {reason} after 3 attempts. Retry the prepared release; no rebuild is needed") from None
        print(f"Upload interrupted for {label}; retrying the same file", flush=True)
        time.sleep(attempt)


def confirm_release(args, release, token):
    result = api_request(args.api, "/users/me/platform-releases/confirm", {"releaseId": release["releaseId"]}, token)
    if result.get("state") != "published" or result.get("release") != release:
        raise ValueError("Confirmed release does not match the prepared manifest")
    write(args.release.with_name("published-release.json"), result)
    return result


def api_request(base, route, body, token, method="POST"):
    uri = urllib.parse.urlparse(base)
    if uri.scheme != "https" or not uri.hostname or uri.username or uri.query or uri.fragment:
        raise ValueError("API must be a non-secret HTTPS base URL")
    request = urllib.request.Request(base.rstrip("/") + route, data=json.dumps(body).encode() if body is not None else None,
                                    headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"}, method=method)
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # Never print presigned URLs or tokens. The response body is bounded for actionable API errors.
        raise APIError(error.code, f"API {error.code}: {error.read(1024).decode(errors='replace')}") from None

def publish(args):
    release = json.loads(args.release.read_text())
    if args.require_kind and release.get("kind") != args.require_kind:
        raise ValueError("Select a " + args.require_kind + " release for this uploader")
    if args.require_platform and not any(v.get("platform") == args.require_platform for v in release.get("variants", [])):
        raise ValueError("Selected release does not include " + args.require_platform)
    sources = json.loads(args.release.with_name("local-sources.json").read_text())
    check_release(release, sources)
    if args.dry_run:
        print("Validated local release; no network requests made")
        return
    token = os.environ.get(args.token_env)
    if not token:
        raise ValueError("Set " + args.token_env + " to an authenticated publisher token")
    if getattr(args, "resume", False):
        print("Checking whether the prepared release is already complete", flush=True)
        try:
            confirm_release(args, release, token)
            print("Confirmed existing immutable release " + release["releaseId"], flush=True)
            return
        except APIError as error:
            if error.code not in (400, 404): raise
    print("Reserving uploads for prepared release " + release["releaseId"], flush=True)
    response = api_request(args.api, "/users/me/platform-releases/upload-urls", release, token)
    if release["kind"] in ("catalog", "asset-pack"):
        for variant in release["variants"]:
            evidence = json.loads((Path(sources[variant["platform"]]) / "platform-build.json").read_text())
            expected_base = response["contentBaseUrls"][variant["platform"]].rstrip("/")
            if evidence.get("contentBaseUrl", "").rstrip("/") != expected_base:
                raise ValueError("Catalog bundle URLs do not match API storage; rebuild using the publisher's actual owner/CDN origin")
    expected = {(v["platform"], f["path"]): f for v in release["variants"] for f in v["files"]}
    got = {(u["platform"],u["path"]) for u in response["uploads"]}
    if got != set(expected) or len(got) != len(response["uploads"]):
        raise ValueError("Upload reservation does not match the prepared release")
    for upload in response["uploads"]:
        platform, name = upload["platform"], upload["path"]
        checksum = base64.b64encode(bytes.fromhex(expected[(platform,name)]["sha256"])).decode()
        if upload["checksumSHA256"] != checksum:
            raise ValueError("Upload checksum mismatch")
        uri = urllib.parse.urlparse(upload["url"])
        if uri.scheme != "https" or uri.username:
            raise ValueError("Upload URL must use HTTPS")
        path = Path(sources[platform]) / name
        upload_file(upload["url"], path,
            {"Content-Length": str(expected[(platform,name)]["bytes"]), "x-amz-checksum-sha256": checksum,
             "Content-Type": upload["contentType"], "Cache-Control": "public, max-age=31536000, immutable"}, platform + "/" + name)
    print("Confirming uploaded release " + release["releaseId"], flush=True)
    confirm_release(args, release, token)
    print("Published immutable release " + release["releaseId"] + "; event routing unchanged", flush=True)


def world_mobile(args):
    """Automatic iOS companion to the existing desktop/server World Publisher job."""
    job = json.loads(args.job.read_text())
    if job.get("kind") != "world" or not job.get("scene"):
        raise ValueError("Select a main world scene")
    package = json.loads((args.sdk_root / "package.json").read_text())
    if package.get("name") != "com.virtualvenues.sdk" or not safe_id(package.get("version", "")):
        raise ValueError("Missing SDK compatibility version")
    source, destination = args.source.resolve(), args.output.resolve()
    if destination == source or source in destination.parents and destination.relative_to(source).parts[0] != "Builds":
        raise ValueError("Keep mobile outputs outside authoring inputs; use Builds or an external directory")
    output = new_output(args.output)
    worker = output / "worker"
    create_worker(argparse.Namespace(source=args.source, destination=worker, sdk_root=args.sdk_root))
    build_args = argparse.Namespace(output=output / "release", job=args.job,
        project_ios=worker, project_desktop=None, project_server=None, unity=args.unity,
        asset_id=job["assetId"], version=job["version"], runtime_version="sdk-" + package["version"],
        release_id=None, core_profile=False, content_origin=None, owner_id=None)
    release = build_content(build_args)
    if args.publish:
        publish(argparse.Namespace(release=build_args.output / "release.json", api=args.api,
            token_env="VV_PUBLISH_TOKEN", dry_run=False, require_kind="world", require_platform="ios"))
    write(output / "world-result.json", {"releaseId": release["releaseId"], "published": args.publish})
    print("Mobile world ready; desktop/server publishing will continue")

def main(player_builder=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    for command in (("build-content", "prepare", "build-player") if player_builder else ("build-content", "prepare")):
        p = sub.add_parser(command)
        p.add_argument("--asset-id", required=True)
        p.add_argument("--version", required=True)
        p.add_argument("--runtime-version", required=True)
        p.add_argument("--release-id")
        p.add_argument("--output", type=Path, required=True)
        p.add_argument("--core-profile", action="store_true")
        if command == "prepare":
            p.add_argument("--kind", choices=("world", "catalog", "asset-pack", "player"), required=True)
            p.add_argument("--desktop", type=Path)
            p.add_argument("--ios", type=Path)
            p.add_argument("--server", type=Path)
        else:
            p.add_argument("--project-desktop", type=Path)
            p.add_argument("--project-ios", type=Path)
            p.add_argument("--unity", type=Path, default=UNITY)
        if command == "build-content":
            p.add_argument("--job", type=Path, required=True)
            p.add_argument("--project-server", type=Path)
            p.add_argument("--content-origin")
            p.add_argument("--owner-id")
        if command == "build-player":
            p.add_argument("--event-catalog", type=Path)
            p.add_argument("--team")
    p = sub.add_parser("world-mobile")
    p.add_argument("--source", type=Path, required=True)
    p.add_argument("--sdk-root", type=Path, required=True)
    p.add_argument("--job", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--unity", type=Path, default=UNITY)
    p.add_argument("--publish", action="store_true")
    p.add_argument("--api")
    p = sub.add_parser("create-worker")
    p.add_argument("--source", type=Path, required=True)
    p.add_argument("--destination", type=Path, required=True)
    p.add_argument("--sdk-root", type=Path)
    p = sub.add_parser("publish")
    p.add_argument("--release", type=Path, required=True)
    p.add_argument("--api", required=True)
    p.add_argument("--token-env", default="VV_PUBLISH_TOKEN")
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--resume", action="store_true", help="Confirm an existing immutable release before retrying its draft uploads")
    p.add_argument("--require-kind", choices=("world", "catalog", "asset-pack", "player"))
    p.add_argument("--require-platform", choices=tuple(TARGETS))
    p = sub.add_parser("pin-event")
    p.add_argument("--api", required=True)
    p.add_argument("--event-id", required=True)
    p.add_argument("--platform", choices=("desktop-web", "ios"), required=True)
    p.add_argument("--pins", type=Path, required=True)
    p.add_argument("--token-env", default="VV_PUBLISH_TOKEN")
    args = parser.parse_args()
    try:
        if args.command == "world-mobile":
            if args.publish and not args.api:
                raise ValueError("Publication needs an API destination")
            world_mobile(args)
        elif args.command == "create-worker":
            create_worker(args)
        elif args.command == "build-content":
            build_content(args)
        elif args.command == "build-player":
            player_builder(args)
        elif args.command == "prepare":
            folders = {key: value for key,value in (("desktop-web",args.desktop),("ios",args.ios),("linux-server",args.server)) if value}
            if not folders:
                raise ValueError("Select at least one platform folder")
            manifest_for(args,new_output(args.output),folders)
        elif args.command == "publish":
            publish(args)
        else:
            token = os.environ.get(args.token_env)
            if not token or not safe_id(args.event_id):
                raise ValueError("Set publisher token and use a valid event ID")
            result = api_request(args.api, f"/vv/events/{args.event_id}/platform-releases/{args.platform}", json.loads(args.pins.read_text()), token, "PUT")
            print(json.dumps(result,indent=2))
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        parser.exit(1,str(error) + "\n")

if __name__ == "__main__":
    main()
