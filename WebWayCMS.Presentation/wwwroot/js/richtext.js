// Rich-text editor interop for the Blazor admin (Interactive Server).
//
// CKEditor mutates the DOM it is attached to, which conflicts with Blazor's diffing. The Razor
// RichTextEditor component therefore renders an opaque host element that Blazor never re-renders,
// and this module owns the CKEditor instance entirely. Content flows back to .NET via a change
// callback (so the bound model stays current without Blazor touching the editor DOM).
// Imported from esm.sh (the npm distribution served as self-contained browser ESM, with bare
// dependency specifiers resolved) rather than the CKEditor cloud CDN: the 'GPL' license key is only
// valid for the npm/self-hosted distribution channel, not the cloud channel.
import {
	ClassicEditor,
	Essentials, Bold, Italic, Underline, Strikethrough, Code, BlockQuote,
	Heading, Link, List, Indent, IndentBlock, Paragraph,
	Font, FontSize, FontFamily, FontColor, FontBackgroundColor, Alignment,
	Table, TableToolbar, CodeBlock, HorizontalLine, SpecialCharacters,
	RemoveFormat, Highlight, SourceEditing, HtmlEmbed, GeneralHtmlSupport
} from 'https://esm.sh/ckeditor5@46.1.1';

// The license key is supplied server-side via a meta tag (from CKEditor:LicenseKey config).
// Fall back to 'GPL' when none is configured so the editor still loads in dev/integration.
function licenseKey() {
	const meta = document.querySelector('meta[name="ckeditor-license-key"]');
	const key = meta && meta.content ? meta.content : '';
	return key || 'GPL';
}

export async function init(element, dotnetRef, initialValue) {
	const editor = await ClassicEditor.create(element, {
		licenseKey: licenseKey(),
		initialData: initialValue || '',
		plugins: [
			Essentials, Bold, Italic, Underline, Strikethrough, Code, BlockQuote,
			Heading, Link, List, Indent, IndentBlock, Paragraph,
			Font, FontSize, FontFamily, FontColor, FontBackgroundColor, Alignment,
			Table, TableToolbar, CodeBlock, HorizontalLine, SpecialCharacters,
			RemoveFormat, Highlight, SourceEditing, HtmlEmbed, GeneralHtmlSupport
		],
		toolbar: [
			'htmlEmbed',
			'undo', 'redo', '|',
			'heading', '|',
			'bold', 'italic', 'underline', 'strikethrough', 'removeFormat', '|',
			'fontSize', 'fontFamily', 'fontColor', 'fontBackgroundColor', 'highlight', '|',
			'link', 'bulletedList', 'numberedList', 'indent', '|',
			'blockQuote', 'code', 'codeBlock', '|',
			'insertTable', 'horizontalLine', '|',
			'specialCharacters', '|',
			'alignment', '|',
			'sourceEditing'
		],
		table: {
			contentToolbar: ['tableColumn', 'tableRow', 'mergeTableCells']
		},
		htmlSupport: {
			allow: [{ name: /.*/, attributes: true, classes: true, styles: true }]
		}
	});

	// Push edits back to the bound .NET model.
	editor.model.document.on('change:data', () => {
		dotnetRef.invokeMethodAsync('OnContentChanged', editor.getData());
	});

	// Returned to Blazor as an IJSObjectReference so the component can tear the editor down.
	return {
		destroy: () => editor.destroy()
	};
}
