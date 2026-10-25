import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import argparse
import os
import platform_publisher as publisher

class PublishingContracts(unittest.TestCase):
    def release(self, variant):
        return {'schemaVersion':1,'releaseId':'probe-1','assetId':'world','kind':'world','version':'1','runtimeVersion':'native-v1','variants':[variant]}
    def fixture(self, root, platform='ios'):
        root.mkdir()
        (root/'world_base').write_bytes(b'bundle fixture')
        publisher.write(root/'world.streaming.json', {'target':publisher.TARGETS[platform], 'bundles':[{'file':'world_base','bytes':14}]})
        publisher.write(root/'native-validation.json', {'result':'passed'})
        publisher.write(root/'platform-build.json', {'result':'Succeeded','platform':platform,'target':publisher.TARGETS[platform],'unityVersion':'6000.6.0f1','entryPoint':'world.streaming.json'})
        return publisher.collect_variant(root,platform,'world')
    def test_target_mismatch_cannot_be_prepared(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'ios';self.fixture(root)
            manifest=json.loads((root/'world.streaming.json').read_text());manifest['target']='WebGL';publisher.write(root/'world.streaming.json',manifest)
            with self.assertRaises(ValueError):publisher.collect_variant(root,'ios','world')
    def test_modified_bundle_blocks_publication(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'ios';variant=self.fixture(root)
            release=self.release(variant);sources={'ios':str(root)}
            publisher.check_release(release,sources)
            (root/'world_base').write_bytes(b'different bytes')
            with self.assertRaises(ValueError):publisher.check_release(release,sources)
    def test_unvalidated_world_cannot_be_prepared(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'ios';self.fixture(root)
            publisher.write(root/'native-validation.json',{'result':'failed'})
            with self.assertRaises(ValueError):publisher.collect_variant(root,'ios','world')
    def test_symlink_cannot_escape_release(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'ios';variant=self.fixture(root)
            outside=Path(temp)/'outside';outside.write_bytes(b'bundle fixture')
            (root/'world_base').unlink();(root/'world_base').symlink_to(outside)
            with self.assertRaises(ValueError):publisher.check_release(self.release(variant),{'ios':str(root)})
    def test_generated_bundle_names_and_traversal(self):
        self.assertTrue(publisher.safe_path('vvsdkavatars_' + 'a'*150 + '.bundle'))
        self.assertFalse(publisher.safe_path('../escape.bundle'))
        self.assertFalse(publisher.safe_path('/absolute.bundle'))
    def test_mobile_uploader_rejects_world_release_before_network(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'ios';variant=self.fixture(root)
            release=Path(temp)/'release.json';publisher.write(release,self.release(variant))
            args=type('Args',(),{'release':release,'require_kind':'player','require_platform':'ios'})()
            with self.assertRaisesRegex(ValueError,'Select a player release'):publisher.publish(args)
    def test_empty_release_cannot_validate(self):
        with self.assertRaises(ValueError):publisher.validate_manifest({'variants':[]})
    def test_output_cannot_modify_authoring_inputs_or_worker(self):
        with tempfile.TemporaryDirectory() as temp:
            source=Path(temp)/'source';(source/'Assets').mkdir(parents=True)
            worker=Path(temp)/'worker';worker.mkdir()
            publisher.write(worker/'.vv-disposable-project.json',{'sourceProject':str(source)})
            for output in [source/'Assets'/'Builds', source/'ProjectSettings'/'release', worker/'release']:
                with self.assertRaises(ValueError):publisher.guard_output(output,[worker])
                self.assertFalse(output.exists())
            publisher.guard_output(source/'Builds'/'release',[worker])
    def test_existing_worker_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);source=root/'source';source.mkdir();destination=root/'worker';destination.mkdir()
            args=type('Args',(),{'source':source,'destination':destination})()
            with self.assertRaises(ValueError):publisher.create_worker(args)
            self.assertTrue(destination.exists())

class WorkerPackageContracts(unittest.TestCase):
    def test_package_worker_embeds_sdk_and_preserves_source(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);source=root/'creator';package=root/'cache'/'sdk';worker=root/'worker'
            for name in ('Assets','Packages','ProjectSettings'):(source/name).mkdir(parents=True)
            package.mkdir(parents=True)
            publisher.write(package/'package.json',{'name':'com.virtualvenues.sdk'})
            (package/'script.cs.meta').write_text('guid: original-guid')
            publisher.write(source/'Packages/manifest.json',{'dependencies':{'com.virtualvenues.sdk':'git-sdk','local':'file:../../local'}})
            args=type('Args',(),{'source':source,'destination':worker,'sdk_root':package})()
            publisher.create_worker(args)
            self.assertEqual((worker/'Packages/com.virtualvenues.sdk/script.cs.meta').read_text(),'guid: original-guid')
            self.assertEqual(json.loads((worker/'Packages/manifest.json').read_text())['dependencies']['local'],'file:'+str((source/'Packages/../../local').resolve()))
            self.assertEqual(json.loads((source/'Packages/manifest.json').read_text())['dependencies']['local'],'file:../../local')
    def test_assets_sdk_does_not_duplicate_package(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);source=root/'core';worker=root/'worker'
            for name in ('Assets/sdk','Packages','ProjectSettings'):(source/name).mkdir(parents=True)
            publisher.write(source/'Assets/sdk/package.json',{'name':'com.virtualvenues.sdk'})
            publisher.write(source/'Packages/manifest.json',{'dependencies':{}})
            publisher.create_worker(type('Args',(),{'source':source,'destination':worker,'sdk_root':source/'Assets/sdk'})())
            self.assertTrue((worker/'Assets/sdk/package.json').exists())
            self.assertFalse((worker/'Packages/com.virtualvenues.sdk').exists())
    def test_invalid_sdk_does_not_create_worker(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);source=root/'creator';package=root/'wrong';worker=root/'worker'
            for name in ('Assets','Packages','ProjectSettings'):(source/name).mkdir(parents=True)
            package.mkdir();publisher.write(package/'package.json',{'name':'wrong'})
            with self.assertRaises(ValueError):publisher.create_worker(type('Args',(),{'source':source,'destination':worker,'sdk_root':package})())
            self.assertFalse(worker.exists())

    def test_mobile_checkbox_automatically_selects_isolated_ios_and_sdk_version(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); sdk = root/'sdk'; sdk.mkdir()
            publisher.write(sdk/'package.json', {'name':'com.virtualvenues.sdk','version':'0.10.5'})
            job = root/'job.json'
            publisher.write(job, {'kind':'world','scene':'Assets/Main.unity','additionalScenes':['Assets/Extra.unity'],'assetId':'world-1','version':'build-1'})
            args = argparse.Namespace(source=root/'source', sdk_root=sdk, job=job, output=root/'output', unity=root/'Unity', publish=False, api=None)
            make_worker = lambda a: a.destination.mkdir(parents=True)
            with mock.patch.object(publisher, 'create_worker', side_effect=make_worker) as worker, mock.patch.object(publisher, 'build_content', return_value={'releaseId':'release-1'}) as build, mock.patch.object(publisher,'publish') as upload:
                publisher.world_mobile(args)
                worker.assert_called_once()
                self.assertFalse((args.output/'worker').exists())
                selected = build.call_args.args[0]
                self.assertEqual(selected.project_ios, args.output.resolve()/'worker')
                self.assertIsNone(selected.project_desktop)
                self.assertIsNone(selected.project_server)
                self.assertEqual(selected.runtime_version,'sdk-0.10.5')
                self.assertEqual(json.loads(selected.job.read_text())['additionalScenes'],['Assets/Extra.unity'])
                upload.assert_not_called()
                self.assertEqual(json.loads((args.output/'world-result.json').read_text()), {'releaseId':'release-1','published':False})
    def test_mobile_failure_never_writes_ready_marker_or_uploads(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); sdk = root/'sdk'; sdk.mkdir()
            publisher.write(sdk/'package.json', {'name':'com.virtualvenues.sdk','version':'0.10.5'})
            job = root/'job.json'; publisher.write(job, {'kind':'world','scene':'Assets/Main.unity','assetId':'world-1','version':'build-1'})
            args = argparse.Namespace(source=root/'source', sdk_root=sdk, job=job, output=root/'output', unity=root/'Unity', publish=True, api='https://api.test')
            make_worker = lambda a: a.destination.mkdir(parents=True)
            with mock.patch.object(publisher,'create_worker',side_effect=make_worker), mock.patch.object(publisher,'build_content',side_effect=ValueError('failed')), mock.patch.object(publisher,'publish') as upload:
                with self.assertRaises(ValueError): publisher.world_mobile(args)
                upload.assert_not_called()
                self.assertTrue((args.output/'worker').exists())
                self.assertFalse((args.output/'world-result.json').exists())
    def test_unity_workers_do_not_receive_the_publishing_token(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); source=root/'source'; (source/'Assets').mkdir(parents=True)
            worker=root/'worker'; worker.mkdir(); publisher.write(worker/'.vv-disposable-project.json',{'sourceProject':str(source)})
            job=root/'job.json'; publisher.write(job,{'kind':'world','scene':'Assets/Main.unity'})
            args=argparse.Namespace(output=root/'output',job=job,project_ios=worker,project_desktop=None,project_server=None,release_id='release-1',asset_id='world-1',version='1',runtime_version='sdk-0.10.5',unity=root/'Unity',core_profile=False)
            with mock.patch.dict(os.environ, {'VV_PUBLISH_TOKEN':'private-test-value'}), mock.patch.object(publisher,'run_unity') as run, mock.patch.object(publisher,'manifest_for'):
                publisher.build_content(args)
                self.assertNotIn('VV_PUBLISH_TOKEN',run.call_args.args[-1])

    def test_mobile_output_guard_runs_before_creating_authoring_directories(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp); source=root/'source'; sdk=root/'sdk'; sdk.mkdir()
            publisher.write(sdk/'package.json',{'name':'com.virtualvenues.sdk','version':'0.10.5'})
            job=root/'job.json';publisher.write(job,{'kind':'world','scene':'Assets/Main.unity','assetId':'world-1','version':'1'})
            output=source/'Assets'/'MobileOutput'
            args=argparse.Namespace(source=source,sdk_root=sdk,job=job,output=output,publish=False)
            with self.assertRaises(ValueError):publisher.world_mobile(args)
            self.assertFalse(output.exists())


class UploadRecoveryContracts(unittest.TestCase):
    def test_truncated_signature_error_still_reports_safe_code(self):
        error=publisher.UploadHTTPError(403,b'<Error><Code>SignatureDoesNotMatch</Code><Message>PRIVATE'+b'x'*8192)
        self.assertEqual(str(error),'HTTP 403 SignatureDoesNotMatch')

    def test_legacy_hoisted_checksum_fails_before_sending_archive(self):
        with mock.patch.object(publisher.http.client,'HTTPSConnection') as connection:
            with self.assertRaisesRegex(ValueError,'Publisher API update required'):
                publisher.upload_once('https://storage.test/asset?X-Amz-Checksum-Sha256=abc%2Bdef%3D',Path('archive.zip'),{'x-amz-checksum-sha256':'abc+def='})
            connection.assert_not_called()

    def test_signed_checksum_header_is_sent_to_storage(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'archive.zip';path.write_bytes(b'archive')
            connection=mock.Mock()
            connection.getresponse.return_value=mock.Mock(status=200,read=mock.Mock(return_value=b''))
            with mock.patch.object(publisher.urllib.request,'getproxies',return_value={}),mock.patch.object(publisher.http.client,'HTTPSConnection',return_value=connection):
                publisher.upload_once('https://storage.test/asset?X-Amz-SignedHeaders=host%3Bx-amz-checksum-sha256',path,{'x-amz-checksum-sha256':'abc+def='})
            self.assertEqual(connection.request.call_args.kwargs['headers']['x-amz-checksum-sha256'],'abc+def=')

    def test_presigned_checksum_mismatch_is_rejected_before_upload(self):
        with mock.patch.object(publisher.http.client,'HTTPSConnection') as connection:
            with self.assertRaisesRegex(ValueError,'checksum mismatch'):
                publisher.upload_once('https://storage.test/asset?x-amz-checksum-sha256=different',Path('archive.zip'),{'x-amz-checksum-sha256':'expected'})
            connection.assert_not_called()

    def test_interrupted_upload_reopens_file_from_start_and_closes_connections(self):
        import http.client
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'archive.zip';path.write_bytes(b'complete archive')
            first,second=mock.Mock(),mock.Mock();sent=[]
            def fail(*args,**kwargs):
                sent.append(kwargs['body'].read());raise BrokenPipeError(32,'broken pipe')
            def succeed(*args,**kwargs):sent.append(kwargs['body'].read())
            first.request.side_effect=fail;first.getresponse.side_effect=http.client.RemoteDisconnected()
            second.request.side_effect=succeed;second.getresponse.return_value=mock.Mock(status=200,read=mock.Mock(return_value=b''))
            with mock.patch.object(publisher.urllib.request,'getproxies',return_value={}),mock.patch.object(publisher.http.client,'HTTPSConnection',side_effect=[first,second]),mock.patch.object(publisher.time,'sleep'):
                publisher.upload_file('https://storage.test/asset?signature=PRIVATE',path,{'Content-Length':str(path.stat().st_size)},'ios/archive.zip')
            self.assertEqual(sent,[b'complete archive',b'complete archive'])
            first.close.assert_called_once();second.close.assert_called_once()

    def test_early_storage_rejection_reports_safe_error_code_instead_of_broken_pipe(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'archive.zip';path.write_bytes(b'archive')
            connection=mock.Mock();connection.request.side_effect=BrokenPipeError(32,'broken pipe')
            connection.getresponse.return_value=mock.Mock(status=403,read=mock.Mock(return_value=b'<Error><Code>AccessDenied</Code><Message>PRIVATE URL OR SIGNATURE</Message></Error>'))
            with mock.patch.object(publisher.urllib.request,'getproxies',return_value={}),mock.patch.object(publisher.http.client,'HTTPSConnection',return_value=connection),mock.patch.object(publisher.time,'sleep') as sleep:
                with self.assertRaisesRegex(ValueError,'HTTP 403 AccessDenied') as error:
                    publisher.upload_file('https://storage.test/asset?signature=PRIVATE',path,{},'ios/archive.zip')
                self.assertNotIn('PRIVATE',str(error.exception));sleep.assert_not_called()

    def test_retry_limit_hides_sensitive_exception_details(self):
        with mock.patch.object(publisher,'upload_once',side_effect=OSError('PRIVATE PRESIGNED URL')),mock.patch.object(publisher.time,'sleep'):
            with self.assertRaisesRegex(ValueError,'after 3 attempts') as error:
                publisher.upload_file('https://storage.test/private',Path('archive.zip'),{},'ios/archive.zip')
            self.assertNotIn('PRIVATE',str(error.exception))

    def test_confirmed_resume_never_reserves_or_uploads_again(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'ios';variant=PublishingContracts().fixture(root)
            release=PublishingContracts().release(variant);manifest=Path(temp)/'release.json';publisher.write(manifest,release)
            publisher.write(Path(temp)/'local-sources.json',{'ios':str(root)})
            args=argparse.Namespace(release=manifest,require_kind=None,require_platform='ios',dry_run=False,token_env='VV_PUBLISH_TOKEN',api='https://api.test',resume=True)
            with mock.patch.dict(os.environ,{'VV_PUBLISH_TOKEN':'private-token'}),mock.patch.object(publisher,'api_request',return_value={'state':'published','release':release}) as api,mock.patch.object(publisher,'upload_file') as upload:
                publisher.publish(args)
            self.assertEqual(api.call_count,1);self.assertIn('/confirm',api.call_args.args[1]);upload.assert_not_called()
            self.assertTrue(manifest.with_name('published-release.json').exists())

    def test_confirmed_manifest_mismatch_never_writes_publication_receipt(self):
        with tempfile.TemporaryDirectory() as temp:
            args=argparse.Namespace(api='https://api.test',release=Path(temp)/'release.json')
            with mock.patch.object(publisher,'api_request',return_value={'state':'published','release':{'releaseId':'different'}}):
                with self.assertRaisesRegex(ValueError,'does not match'):publisher.confirm_release(args,{'releaseId':'expected'},'token')
            self.assertFalse(args.release.with_name('published-release.json').exists())

if __name__=='__main__': unittest.main()
